import copy
from collections import Counter
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import ci_tests


def case(name="Example.Test", cls="Example", state="Runnable"):
    return {"class": cls, "name": name, "state": state}


def plan_for(cases):
    group = {"id": "integration-1", "assembly": "Content.IntegrationTests",
             "classes": sorted({item["class"] for item in cases}), "cases": cases}
    return {"seed": 42, "groups": [group], "excluded": [],
            "catalog": [{"assembly": group["assembly"], **item} for item in cases]}


class CiTests(unittest.TestCase):
    def test_incomplete_runtime_fails_before_starting_nunit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(ValueError, "bin/Content.Client/Content.Client.dll"):
                ci_tests.verify_runtime(root)
            for name in (*ci_tests.ASSEMBLIES, "Content.Client", "Content.Server"):
                dll = root / "bin" / name / f"{name}.dll"
                dll.parent.mkdir(parents=True)
                dll.touch()
            for name in ("Resources/Prototypes", "RobustToolbox/Resources"):
                (root / name).mkdir(parents=True)
            ci_tests.verify_runtime(root)

    def test_inherited_explicit_and_ignored_states(self):
        root = ET.fromstring('<test-suite runstate="Explicit"><test-case classname="A" '
                             'fullname="A.Test" runstate="Runnable"/></test-suite>')
        self.assertEqual(list(ci_tests.discovered_cases(root))[0]["state"], "Explicit")
        root.set("runstate", "Ignored")
        self.assertEqual(list(ci_tests.discovered_cases(root))[0]["state"], "Ignored")

    def test_discovery_rejects_unnamed_case(self):
        with self.assertRaises(ValueError):
            list(ci_tests.discovered_cases(ET.fromstring('<test-case name="Test"/>')))

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

    def inspect(self, cases, results, *, exit_code=0, suite_failure=False):
        group = plan_for(cases)["groups"][0]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "execution.json").write_text(json.dumps({"group": group["id"], "exit_code": exit_code}))
            (output / "results.trx").write_text("<TestRun/>")
            root = ET.Element("test-run")
            suite = ET.SubElement(root, "test-suite", fullname="Fixture")
            if suite_failure:
                ET.SubElement(ET.SubElement(suite, "failure"), "message").text = "OneTimeSetUp failed"
            for item, outcome in results:
                ET.SubElement(suite, "test-case", classname=item["class"], fullname=item["name"],
                              result=outcome, duration="1")
            ET.ElementTree(root).write(output / "Content.IntegrationTests.xml")
            return ci_tests.inspect_group(group, output)[0]

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
