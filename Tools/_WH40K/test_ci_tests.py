import copy
from collections import Counter
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import ci_tests


def case(name="Example.Test", cls="Example", state="Runnable"):
    return {"class": cls, "name": name, "state": state}


def plan_for(cases):
    group = {"id": "integration-1", "assembly": "Content.IntegrationTests",
             "classes": sorted({item["class"] for item in cases}), "cases": cases}
    return {"seed": 42, "groups": [group], "excluded": [],
            "catalog": [{"assembly": group["assembly"], **item} for item in cases]}


def make_runtime(root):
    for name in (*ci_tests.ASSEMBLIES, "Content.Client", "Content.Server"):
        dll = root / "bin" / name / f"{name}.dll"
        dll.parent.mkdir(parents=True)
        dll.touch()
    for name in ("Resources/Prototypes", "RobustToolbox/Resources"):
        (root / name).mkdir(parents=True)


class CiTests(unittest.TestCase):
    def test_build_ready_requires_every_preparation_stage(self):
        values = {name: "success" for name in ("VERIFY_RESULT", "RESTORE_RESULT", "COMPILE_RESULT", "PLAN_RESULT", "ARCHIVE_RESULT", "RUNTIME_RESULT")}
        for missing in (None, "PLAN_RESULT", "RUNTIME_RESULT"):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as directory:
                env = dict(values)
                if missing:
                    env.pop(missing)
                with patch.dict(os.environ, env, clear=True):
                    ci_tests.record_build(SimpleNamespace(output=Path(directory)))
                self.assertEqual(ci_tests.read_json(Path(directory) / "execution.json")["ready"], missing is None)

    def test_crash_without_xml_keeps_executor_error_in_copyable_report(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            root = Path(directory)
            args = self.report_fixture(root, missing=True)
            (root / "runs/integration-1/console.log").write_text("System.OutOfMemoryException: simulated crash\n", encoding="utf-8")
            with redirect_stdout(StringIO()):
                ci_tests.report(args)
            self.assertIn("OutOfMemoryException", (root / "final/errors.txt").read_text(encoding="utf-8"))

    def report_fixture(self, root, *, failed=False, missing=False, build_failed=False, defer=True):
        plan = plan_for([case()])
        ci_tests.write_json(root / "plan.json", plan)
        ci_tests.write_json(root / "build/execution.json", {"ready": not build_failed,
                            "stages": {"Compile": "failure" if build_failed else "success"}})
        if build_failed:
            (root / "build/build.log").write_text("example.cs(1): error CS0000: compiler failure\n", encoding="utf-8")
        group = root / "runs/integration-1"
        group.mkdir(parents=True)
        ci_tests.write_json(group / "execution.json", {"group": "integration-1", "seed": 42,
                            "exit_code": 1 if failed else 0})
        (group / "results.trx").write_text("<TestRun/>")
        xml = ET.Element("test-run")
        node = ET.SubElement(xml, "test-case", classname="Example", fullname="Example.Test",
                             result="Failed" if failed else "Passed", duration="1")
        if failed:
            failure = ET.SubElement(node, "failure")
            ET.SubElement(failure, "message").text = "full error " + "x" * 1800
            ET.SubElement(failure, "stack-trace").text = "at Example.Test() in example.cs:line 99"
        if not missing:
            ET.ElementTree(xml).write(group / "Content.IntegrationTests.xml")
        return SimpleNamespace(plan=root / "plan.json", build_status=root / "build/execution.json",
                               results=root / "runs", timings=root / "timings.json", output=root / "final",
                               defer_verdict=defer)

    def test_deferred_failure_is_reported_by_only_the_final_verdict(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            root = Path(directory)
            args = self.report_fixture(root, failed=True)
            with redirect_stdout(StringIO()):
                self.assertEqual(ci_tests.report(args), 0)
                self.assertEqual(ci_tests.verdict(args), 1)
            text = (root / "final/errors.txt").read_text(encoding="utf-8")
            self.assertIn("x" * 1800, text)
            self.assertIn("example.cs:line 99", text)
            self.assertTrue((root / "timings.json").is_file())

    def test_successful_final_verdict_requires_all_reports(self):
        for missing in (False, True):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
                root = Path(directory)
                args = self.report_fixture(root, missing=missing)
                with redirect_stdout(StringIO()):
                    self.assertEqual(ci_tests.report(args), 0)
                    self.assertEqual(ci_tests.verdict(args), int(missing))
                self.assertEqual((root / "timings.json").exists(), not missing)

    def test_compiler_failure_survives_green_preparation_job(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"CI_BUILD_RESULT": "success", "CI_BUILD_READY": "false", "CI_TEST_RESULT": "skipped"}, clear=True):
            root = Path(directory)
            args = self.report_fixture(root, build_failed=True)
            with redirect_stdout(StringIO()):
                ci_tests.report(args)
                self.assertEqual(ci_tests.verdict(args), 1)
            self.assertIn("error CS0000", (root / "final/errors.txt").read_text(encoding="utf-8"))
            self.assertFalse((root / "timings.json").exists())

    def test_missing_plan_and_build_record_cannot_pass(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            root = Path(directory)
            args = SimpleNamespace(plan=root / "absent.json", build_status=root / "absent-build.json",
                                   results=root / "runs", timings=root / "timings.json", output=root / "final", defer_verdict=True)
            with redirect_stdout(StringIO()):
                self.assertEqual(ci_tests.report(args), 0)
                self.assertEqual(ci_tests.verdict(args), 1)

    def test_missing_final_archive_cannot_make_check_green(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            args = self.report_fixture(Path(directory))
            with redirect_stdout(StringIO()), redirect_stderr(StringIO()):
                ci_tests.report(args)
                with patch.dict(os.environ, {"CI_REPORT_SAVED": "failure"}):
                    self.assertEqual(ci_tests.verdict(args), 1)

    def test_stale_final_report_cannot_make_check_green(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            args = self.report_fixture(Path(directory))
            with redirect_stdout(StringIO()), redirect_stderr(StringIO()):
                ci_tests.report(args)
                with patch.dict(os.environ, {"GITHUB_RUN_ID": "another-run"}):
                    self.assertEqual(ci_tests.verdict(args), 1)

    def test_upstream_job_failure_is_not_masked_by_complete_xml(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"CI_TEST_RESULT": "failure"}, clear=True):
            args = self.report_fixture(Path(directory))
            with redirect_stdout(StringIO()):
                ci_tests.report(args)
                self.assertEqual(ci_tests.verdict(args), 1)

    def test_backticks_in_failure_cannot_break_copy_block(self):
        text = "an error with ``` and ```` fences"
        self.assertTrue(ci_tests.markdown_block(text).startswith("`````text\n"))

    def test_invalid_cache_falls_back_without_losing_new_classes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "cache.json").write_text("broken json")
            ci_tests.write_json(root / "profile.json", {"Old": 100})
            with redirect_stdout(StringIO()):
                timings = ci_tests.load_timings(root / "cache.json", root / "profile.json")
            self.assertEqual(timings, {"Old": 100})
            groups = ci_tests.split_classes([case(cls="Old"), case(cls="New")], 2, timings)
            self.assertEqual({name for group in groups for name in group}, {"Old", "New"})

    def test_memory_includes_grandchildren_and_ignores_other_processes(self):
        with tempfile.TemporaryDirectory() as directory:
            proc = Path(directory)
            for pid, parent, rss in ((10, 1, 100), (20, 10, 500), (30, 20, 700), (40, 1, 9000)):
                path = proc / str(pid)
                path.mkdir()
                (path / "status").write_text(f"PPid:\t{parent}\nVmRSS:\t{rss} kB\n")
            (proc / "meminfo").write_text("MemTotal: 4096 kB\nMemAvailable: 1024 kB\n")
            self.assertEqual(ci_tests.sample_memory(10, proc), {"rss": 1300 * 1024, "available": 1024**2, "total": 4 * 1024**2})

    def test_integration_gc_budget_reserves_memory_for_native_allocations(self):
        with patch.dict(os.environ, {}, clear=True):
            environment = ci_tests.test_environment("Content.IntegrationTests")
        self.assertEqual(int(environment["DOTNET_GCHeapHardLimitPercent"], 16), 70)
        self.assertEqual(environment["DOTNET_gcServer"], "1")

    def test_explicit_gc_limits_are_preserved(self):
        for key in ("DOTNET_GCHeapHardLimit", "COMPlus_GCHeapHardLimit",
                    "DOTNET_GCHeapHardLimitPercent", "COMPlus_GCHeapHardLimitPercent"):
            with self.subTest(variable=key), patch.dict(os.environ, {key: "40"}, clear=True):
                environment = ci_tests.test_environment("Content.IntegrationTests")
                self.assertEqual(environment[key], "40")
                if key != "DOTNET_GCHeapHardLimitPercent":
                    self.assertNotIn("DOTNET_GCHeapHardLimitPercent", environment)

    def test_unit_tests_keep_their_default_memory_and_parallelism_settings(self):
        group = plan_for([case()])["groups"][0]
        group.update(id="unit", assembly="Content.Tests")
        with patch.dict(os.environ, {}, clear=True):
            environment = ci_tests.test_environment(group["assembly"])
        self.assertNotIn("DOTNET_GCHeapHardLimitPercent", environment)
        self.assertNotIn("NumberOfTestWorkers", ci_tests.nunit_options(group, 42, Path("results")))

    def test_integration_shards_keep_all_classes_and_the_discovery_seed(self):
        for group_id in ("integration-1", "integration-2"):
            with self.subTest(group=group_id):
                group = plan_for([case(), case("Nested.Test", "Example.Nested")])["groups"][0]
                group["id"] = group_id
                options = ci_tests.nunit_options(group, 1754854171, Path("results"))
                self.assertEqual(options["NumberOfTestWorkers"], 1)
                self.assertEqual(options["RandomSeed"], 1754854171)
                self.assertEqual(options["Where"], ci_tests.class_filter(group["classes"]))
                self.assertEqual(options["ExplicitMode"], "None")

    def test_incomplete_runtime_fails_before_starting_nunit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(ValueError, "bin/Content.Client/Content.Client.dll"):
                ci_tests.verify_runtime(root)
            make_runtime(root)
            ci_tests.verify_runtime(root)

    def test_stale_assembly_records_failure_without_starting_testhost(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            make_runtime(root)
            plan = plan_for([case()])
            plan["groups"][0]["sha256"] = "wrong-assembly"
            ci_tests.write_json(root / "plan.json", plan)
            args = SimpleNamespace(plan=root / "plan.json", group="integration-1", output=root / "results")
            with patch.object(ci_tests, "ROOT", root), patch.object(ci_tests.subprocess, "Popen") as start:
                with redirect_stdout(StringIO()), self.assertRaisesRegex(ValueError, "does not match"):
                    ci_tests.run_group(args)
                start.assert_not_called()
            status = ci_tests.read_json(root / "results/integration-1/execution.json")
            self.assertEqual(status["exit_code"], -1)
            self.assertIn("does not match", status["error"])

    def test_inherited_explicit_and_ignored_states(self):
        root = ET.fromstring('<test-suite runstate="Explicit"><test-case classname="A" '
                             'fullname="A.Test" runstate="Runnable"/></test-suite>')
        self.assertEqual(list(ci_tests.discovered_cases(root))[0]["state"], "Explicit")
        root.set("runstate", "Ignored")
        self.assertEqual(list(ci_tests.discovered_cases(root))[0]["state"], "Ignored")

    def test_discovery_rejects_unnamed_case(self):
        with self.assertRaises(ValueError):
            list(ci_tests.discovered_cases(ET.fromstring('<test-case name="Test"/>')))

    def test_invalid_source_without_cases_cannot_disappear_from_plan(self):
        root = ET.fromstring('<test-suite><test-suite type="ParameterizedMethod" fullname="Broken.Source" '
                             'runstate="NotRunnable"><properties><property name="_SKIPREASON" '
                             'value="No arguments"/></properties></test-suite>'
                             '<test-case classname="Good" fullname="Good.Test"/></test-suite>')
        with self.assertRaisesRegex(ValueError, "Broken.Source: No arguments"):
            list(ci_tests.discovered_cases(root))

    def test_nested_namespaces_and_parameterized_classes_have_one_owner(self):
        cases = [case("Root.Test", "Root"), case("Root.Child.Test(1)", "Root.Child"),
                 case("Root.Child.Test(2)", "Root.Child"), case("Root+Nested.Test", "Root+Nested")]
        buckets = ci_tests.split_classes(cases, 2, {})
        self.assertEqual(Counter(name for bucket in buckets for name in bucket),
                         Counter({"Root": 1, "Root.Child": 1, "Root+Nested": 1}))
        self.assertEqual(buckets, ci_tests.split_classes(list(reversed(cases)), 2, {}))

    def test_measured_durations_balance_groups(self):
        cases = [case(cls=name) for name in "ABCD"]
        self.assertEqual(ci_tests.split_classes(cases, 2, dict(A=10, B=6, C=4, D=1)),
                         [["A", "D"], ["B", "C"]])

    def test_empty_groups_and_nonfinite_timings_are_rejected(self):
        for count, timings in ((0, {}), (2, {}), (1, {"Example": float("nan")})):
            with self.subTest(count=count, timings=timings), self.assertRaises(ValueError):
                ci_tests.split_classes([case()], count, timings)

    def test_missing_assignment_is_rejected(self):
        plan = plan_for([case(), case("Example.Other")])
        plan["groups"][0]["cases"].pop()
        with self.assertRaises(ValueError):
            ci_tests.validate_plan(plan)

    def test_duplicate_assignment_is_rejected(self):
        plan = plan_for([case()])
        other = copy.deepcopy(plan["groups"][0])
        other["id"] = "integration-2"
        plan["groups"].append(other)
        with self.assertRaises(ValueError):
            ci_tests.validate_plan(plan)

    def test_invalid_group_path_is_rejected(self):
        plan = plan_for([case()])
        plan["groups"][0]["id"] = "../elsewhere"
        with self.assertRaises(ValueError):
            ci_tests.validate_plan(plan)

    def inspect(self, cases, results, *, exit_code=0, suite_failure=False, trx=True, seed=42):
        group = plan_for(cases)["groups"][0]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "execution.json").write_text(json.dumps({"group": group["id"], "exit_code": exit_code, "seed": 42}))
            if trx:
                (output / "results.trx").write_text("<TestRun/>")
            root = ET.Element("test-run")
            suite = ET.SubElement(root, "test-suite", fullname="Fixture")
            if suite_failure:
                ET.SubElement(ET.SubElement(suite, "failure"), "message").text = "OneTimeSetUp failed"
            for item, outcome in results:
                ET.SubElement(suite, "test-case", classname=item["class"], fullname=item["name"],
                              result=outcome, duration="1")
            ET.ElementTree(root).write(output / "Content.IntegrationTests.xml")
            return ci_tests.inspect_group(group, output, seed=seed)[0]

    def test_missing_trx_cannot_pass_with_valid_nunit_xml(self):
        self.assertTrue(any("Missing TRX" in problem for problem in
                            self.inspect([case()], [(case(), "Passed")], trx=False)))

    def test_results_with_another_seed_are_rejected(self):
        self.assertTrue(any("seed" in problem for problem in
                            self.inspect([case()], [(case(), "Passed")], seed=43)))

    def test_unknown_test_outcome_is_rejected(self):
        self.assertTrue(self.inspect([case()], [(case(), "Unknown")]))

    def test_duplicate_display_names_preserve_multiplicity(self):
        cases = [case(), case()]
        self.assertFalse(self.inspect(cases, [(item, "Passed") for item in cases]))
        self.assertTrue(any("Missing: 1" in problem for problem in self.inspect(cases, [(case(), "Passed")])))

    def test_unexpected_test_or_duplicate_result_fails(self):
        problems = self.inspect([case()], [(case(), "Passed"), (case(), "Passed")])
        self.assertTrue(any("Unexpected/duplicate" in problem for problem in problems))

    def test_setup_failure_with_exit_zero_fails(self):
        self.assertTrue(self.inspect([case()], [(case(), "Passed")], suite_failure=True))

    def test_failed_test_and_nonzero_process_fail_independently(self):
        self.assertTrue(self.inspect([case()], [(case(), "Failed")]))
        self.assertTrue(self.inspect([case()], [(case(), "Passed")], exit_code=1))

    def test_empty_execution_and_missing_report_fail(self):
        self.assertTrue(self.inspect([case()], []))
        with tempfile.TemporaryDirectory() as directory:
            problems, *_ = ci_tests.inspect_group(plan_for([case()])["groups"][0], Path(directory))
            self.assertTrue(any("Missing NUnit" in problem for problem in problems))

    def test_ignored_cases_remain_in_coverage(self):
        ignored = case(state="Ignored")
        self.assertFalse(self.inspect([ignored], [(ignored, "Skipped")]))
        self.assertTrue(self.inspect([ignored], []))

    def test_exact_class_filter_escapes_quotes_and_backslashes(self):
        self.assertEqual(ci_tests.class_filter(["Root", "Root.Child", "Outer+Inner"]),
                         "class == 'Root' or class == 'Root.Child' or class == 'Outer+Inner'")
        self.assertEqual(ci_tests.class_filter(["A'B\\C"]), "class == 'A\\'B\\\\C'")


if __name__ == "__main__":
    unittest.main()
