#!/usr/bin/env python3

import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import shlex
import subprocess
import sys
import time
import xml.etree.ElementTree as ET


ASSEMBLIES = ("Content.Tests", "Content.IntegrationTests")
ROOT = Path(__file__).resolve().parents[2]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def assembly_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_runtime(root):
    files = [root / "bin" / name / f"{name}.dll"
             for name in (*ASSEMBLIES, "Content.Client", "Content.Server")]
    directories = [root / "Resources" / "Prototypes", root / "RobustToolbox" / "Resources"]
    missing = [path.relative_to(root).as_posix() for path in files if not path.is_file()]
    missing.extend(path.relative_to(root).as_posix() for path in directories if not path.is_dir())
    if missing:
        raise ValueError("Incomplete test runtime/checkout: " + ", ".join(missing))


def sample_memory(pid, proc=Path("/proc")):
    records = {}
    for path in proc.glob("[0-9]*/status"):
        try:
            fields = dict(line.split(":", 1) for line in path.read_text().splitlines() if ":" in line)
            records[int(path.parent.name)] = (int(fields["PPid"]), int(fields.get("VmRSS", "0").split()[0]) * 1024)
        except (OSError, ValueError, KeyError):
            continue  # A process can exit while /proc is being read.
    children = {pid}
    while True:
        found = {child for child, (parent, _) in records.items() if parent in children} - children
        if not found:
            break
        children.update(found)
    memory = dict(line.split(":", 1) for line in (proc / "meminfo").read_text().splitlines() if ":" in line)
    return {"rss": sum(records[child][1] for child in children if child in records),
            "available": int(memory["MemAvailable"].split()[0]) * 1024,
            "total": int(memory["MemTotal"].split()[0]) * 1024}


def load_timings(*paths):
    for path in paths:
        if path is None or not path.exists():
            continue
        try:
            timings = {name: float(value) for name, value in read_json(path).items()}
            if any(not math.isfinite(value) or value < 0 for value in timings.values()):
                raise ValueError("Invalid duration")
            if timings:
                return timings
        except (OSError, ValueError, TypeError, AttributeError) as error:
            print(f"Ignoring unusable duration cache {path}: {error}", flush=True)
    return {}


def discovered_cases(root, inherited="Runnable"):
    state = root.get("runstate", inherited)
    if inherited in ("Explicit", "Ignored", "NotRunnable"):
        state = inherited
    if state == "NotRunnable":
        reason = root.find("./properties/property[@name='_SKIPREASON']")
        message = reason.get("value", "Invalid test") if reason is not None else "Invalid test"
        raise ValueError(f"NUnit discovery error in {root.get('fullname', root.tag)}: {message}")
    if root.tag == "test-case":
        if not root.get("classname") or not root.get("fullname"):
            raise ValueError("NUnit discovery returned a case without class/fullname")
        yield {"class": root.attrib["classname"], "name": root.attrib["fullname"], "state": state}
    for child in root:
        yield from discovered_cases(child, state)


def split_classes(cases, count, timings):
    classes = sorted({case["class"] for case in cases})
    if not classes or count < 1 or count > len(classes):
        raise ValueError(f"Cannot divide {len(classes)} classes into {count} nonempty groups")
    # Preserve entire classes and their parameterized fixtures. Case counts do not
    # predict the cost of starting/reusing SS14's server/client pool.
    weights = {name: float(timings.get(name, 1)) for name in classes}
    if any(not math.isfinite(weight) or weight < 0 for weight in weights.values()):
        raise ValueError("Invalid test duration")
    weights = {name: max(0.01, weight) for name, weight in weights.items()}
    buckets = [[] for _ in range(count)]
    totals = [0.0] * count
    for name in sorted(classes, key=lambda name: (-weights[name], name)):
        index = min(range(count), key=lambda index: (totals[index], index))
        buckets[index].append(name)
        totals[index] += weights[name]
    return [sorted(bucket) for bucket in buckets]


def prepare(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    (output / "plan.json").unlink(missing_ok=True)
    seed = args.seed if args.seed is not None else secrets.randbelow(2**31 - 1)
    # The checked-in profile bootstraps the first run; subsequent runs use the
    # measured cache. Neither source decides which tests are included.
    timings = load_timings(args.timings, ROOT / "Tools" / "_WH40K" / "ci_test_durations.json")
    plan = {"seed": seed, "groups": [], "excluded": [], "catalog": []}
    for assembly in ASSEMBLIES:
        dll = ROOT / "bin" / assembly / f"{assembly}.dll"
        dump = dll.parent / "Dump" / f"D_{dll.name}.dump"
        # An old discovery dump must never make a failed discovery look successful.
        dump.unlink(missing_ok=True)
        command = ["dotnet", "test", str(dll), "--list-tests", "--",
                   "NUnit.DumpXmlTestDiscovery=true", f"NUnit.RandomSeed={seed}"]
        with (output / f"{assembly}.discovery.log").open("w", encoding="utf-8") as log:
            subprocess.run(command, cwd=ROOT, check=True, stdout=log, stderr=subprocess.STDOUT)
        cases = list(discovered_cases(ET.parse(dump).getroot()))
        if not cases:
            raise ValueError(f"No tests discovered in {assembly}")
        plan["excluded"].extend({"assembly": assembly, **case} for case in cases if case["state"] == "Explicit")
        cases = [case for case in cases if case["state"] != "Explicit"]
        plan["catalog"].extend({"assembly": assembly, **case} for case in cases)
        buckets = split_classes(cases, 1 if assembly == "Content.Tests" else 2, timings)
        for index, classes in enumerate(buckets, 1):
            group_id = "unit" if assembly == "Content.Tests" else f"integration-{index}"
            plan["groups"].append({"id": group_id, "assembly": assembly, "sha256": assembly_hash(dll), "classes": classes,
                                   "cases": [case for case in cases if case["class"] in classes]})
        print(f"{assembly}: {len(cases)} cases, {len(buckets)} group(s)", flush=True)
    validate_plan(plan)
    write_json(output / "plan.json", plan)


def record_build(args):
    stages = {stage: os.environ.get(variable, "missing") for stage, variable in {
        "CI self-checks": "VERIFY_RESULT", "Restore": "RESTORE_RESULT", "Compile": "COMPILE_RESULT",
        "Discovery": "PLAN_RESULT", "Archive": "ARCHIVE_RESULT", "Upload runtime": "RUNTIME_RESULT"}.items()}
    ready = all(result == "success" for result in stages.values())
    write_json(args.output / "execution.json", {"ready": ready, "stages": stages})
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.write(f"ready={str(ready).lower()}\n")


def validate_plan(plan):
    ids = [group["id"] for group in plan["groups"]]
    if not ids or len(ids) != len(set(ids)):
        raise ValueError("Empty plan or duplicate group IDs")
    owners = set()
    for group in plan["groups"]:
        if group["assembly"] not in ASSEMBLIES or not re.fullmatch(r"unit|integration-[1-9][0-9]*", group["id"]):
            raise ValueError("Invalid assembly/group ID")
        classes = set(group["classes"])
        if not group["cases"] or classes != {case["class"] for case in group["cases"]}:
            raise ValueError(f"Empty or inconsistent group: {group['id']}")
        for name in classes:
            key = (group["assembly"], name)
            if key in owners:
                raise ValueError(f"Class assigned more than once: {key}")
            owners.add(key)
    expected = Counter((case["assembly"], case["class"], case["name"]) for case in plan["catalog"])
    actual = Counter((group["assembly"], case["class"], case["name"])
                     for group in plan["groups"] for case in group["cases"])
    if expected != actual:
        raise ValueError("Group assignment does not cover the discovery catalog exactly once")


def class_filter(classes):
    def quote(value):
        return "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"
    return " or ".join(f"class == {quote(name)}" for name in classes)


def test_environment(assembly):
    environment = dict(os.environ, DOTNET_gcServer="1", DOTNET_CLI_UI_LANGUAGE="en")
    limits = ("DOTNET_GCHeapHardLimit", "COMPlus_GCHeapHardLimit",
              "DOTNET_GCHeapHardLimitPercent", "COMPlus_GCHeapHardLimitPercent")
    for key in limits:
        if key not in os.environ:
            continue
        if os.name == "nt":
            environment.pop(key.upper(), None)
        environment[key] = os.environ[key]
    # GC environment numbers are hexadecimal: 0x46 = 70%. Leave room on the
    # runner for native allocations, diagnostics and the OS; respect explicit limits.
    if assembly == "Content.IntegrationTests" and not any(key in environment for key in limits):
        environment["DOTNET_GCHeapHardLimitPercent"] = "46"
    return environment


def nunit_options(group, seed, output):
    options = {"Where": class_filter(group["classes"]), "RandomSeed": seed,
               "ConsoleOut": 0, "MapWarningTo": "Failed", "WorkDirectory": output,
               "TestOutputXml": output, "UseNUnitIdforTestCaseId": "true", "ExplicitMode": "None"}
    # Integration workers retain entire server/client pairs. The two CI shards
    # already run in parallel on separate VMs; keep bulk spawn tests within one VM's RAM.
    if group["assembly"] == "Content.IntegrationTests":
        options["NumberOfTestWorkers"] = 1
    return options


def run_group(args):
    plan = read_json(args.plan)
    validate_plan(plan)
    group = next(group for group in plan["groups"] if group["id"] == args.group)
    dll = ROOT / "bin" / group["assembly"] / f"{group['assembly']}.dll"
    output = (args.output / group["id"]).resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError(f"Refusing to reuse results directory: {output}")
    settings = ET.Element("RunSettings")
    config = ET.SubElement(settings, "RunConfiguration")
    ET.SubElement(config, "TreatNoTestsAsError").text = "true"
    nunit = ET.SubElement(settings, "NUnit")
    options = nunit_options(group, plan["seed"], output)
    for key, value in options.items():
        ET.SubElement(nunit, key).text = str(value)
    settings_path = output / "tests.runsettings"
    ET.ElementTree(settings).write(settings_path, encoding="utf-8", xml_declaration=True)
    command = ["dotnet", "test", str(dll), "--settings", str(settings_path),
               "--results-directory", str(output), "--logger", "trx;LogFileName=results.trx",
               "--blame-crash-dump-type", "mini", "--blame-hang-timeout", "15m",
               "--blame-hang-dump-type", "mini"]
    environment = test_environment(group["assembly"])
    print(f"{group['id']}: {len(group['cases'])} cases; seed {plan['seed']}", flush=True)
    print(shlex.join(command), flush=True)
    started = time.monotonic()
    status = {"group": group["id"], "seed": plan["seed"], "exit_code": -1,
              "nunit_workers": options.get("NumberOfTestWorkers"),
              "started_utc": datetime.now(timezone.utc).isoformat(), "command": command,
              "gc_heap_hard_limit": environment.get("DOTNET_GCHeapHardLimit", environment.get("COMPlus_GCHeapHardLimit")),
              "gc_heap_hard_limit_percent": environment.get("DOTNET_GCHeapHardLimitPercent", environment.get("COMPlus_GCHeapHardLimitPercent"))}
    try:
        verify_runtime(ROOT)
        if assembly_hash(dll) != group.get("sha256"):
            raise ValueError("Test assembly does not match the discovery plan; rebuild and prepare again")
        write_json(output / "execution.json", status)
        with (output / "console.log").open("w", encoding="utf-8") as log:
            with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT) as process:
                next_message = 60
                while True:
                    try:
                        status["exit_code"] = process.wait(timeout=5 if sys.platform == "linux" else 60)
                        break
                    except subprocess.TimeoutExpired:
                        status["seconds"] = time.monotonic() - started
                        if sys.platform == "linux":
                            try:
                                sample = sample_memory(process.pid)
                                status["peak_sampled_tree_rss_bytes"] = max(status.get("peak_sampled_tree_rss_bytes", 0), sample["rss"])
                                status["min_available_memory_bytes"] = min(status.get("min_available_memory_bytes", sample["total"]), sample["available"])
                                status["total_memory_bytes"] = sample["total"]
                            except (OSError, ValueError, KeyError) as error:
                                status["memory_sampling_error"] = str(error)
                        if status["seconds"] >= next_message:
                            memory = ""
                            if "peak_sampled_tree_rss_bytes" in status:
                                memory = (f"; peak sampled RSS {status['peak_sampled_tree_rss_bytes'] / 1024**2:.0f} MiB"
                                          f"; min available {status['min_available_memory_bytes'] / 1024**2:.0f} MiB")
                            print(f"{group['id']}: running for {status['seconds']:.0f}s{memory}", flush=True)
                            write_json(output / "execution.json", status)
                            next_message += 60
    except (OSError, ValueError) as error:
        status["error"] = str(error)
        raise
    finally:
        status["seconds"] = time.monotonic() - started
        status["finished_utc"] = datetime.now(timezone.utc).isoformat()
        write_json(output / "execution.json", status)
    print(f"{group['id']}: exit {status['exit_code']}, {status['seconds']:.1f}s", flush=True)
    problems, *_ = inspect_group(group, output, seed=plan["seed"])
    for problem in problems[:5]:
        print(f"CI error: {problem}", file=sys.stderr)
    if status["exit_code"] and not (output / f"{group['assembly']}.xml").exists():
        print((output / "console.log").read_text(encoding="utf-8", errors="replace")[-12000:])
    return int(bool(problems))


def failure_text(node):
    return "\n".join(part for part in (node.findtext("message", ""), node.findtext("stack-trace", "")) if part)


def inspect_group(group, output, seed=None):
    problems = []
    status_path = output / "execution.json"
    status = read_json(status_path) if status_path.exists() else {}
    if status.get("group") != group["id"] or status.get("exit_code") != 0:
        problems.append(f"Test process failed or did not finish (exit {status.get('exit_code', 'missing')})")
    if status.get("error"):
        problems.append(status["error"])
    if seed is not None and status.get("seed") != seed:
        problems.append("Execution seed does not match the discovery plan")
    if not (output / "results.trx").is_file():
        problems.append("Missing TRX report")
    xml_path = output / f"{group['assembly']}.xml"
    if not xml_path.exists():
        problems.append("Missing NUnit XML report")
        console = output / "console.log"
        if console.exists():
            problems.append("Последние сообщения исполнителя:\n" + console.read_text(encoding="utf-8", errors="replace")[-32000:])
        return problems, status, Counter(), {}, False
    root = ET.parse(xml_path).getroot()
    actual = Counter()
    outcomes = Counter()
    timings = defaultdict(float)
    for case in root.iter("test-case"):
        actual[(case.get("classname"), case.get("fullname"))] += 1
        outcomes[case.get("result")] += 1
        timings[case.get("classname")] += float(case.get("duration", "0"))
        failure = case.find("failure")
        if failure is not None:
            problems.append(f"{case.get('fullname')}:\n{failure_text(failure)}")
    # OneTimeSetUp/OneTimeTearDown failures belong to suites, not necessarily a case.
    for suite in root.iter("test-suite"):
        failure = suite.find("failure")
        if failure is not None and suite.get("site") != "Child":
            problems.append(f"{suite.get('fullname')}:\n{failure_text(failure)}")
    expected = Counter((case["class"], case["name"]) for case in group["cases"])
    for label, difference in (("Missing", expected - actual), ("Unexpected/duplicate", actual - expected)):
        if difference:
            sample = "; ".join(f"{name} (x{count})" for (_, name), count in list(difference.items())[:5])
            problems.append(f"{label}: {sum(difference.values())} cases. {sample}")
    if outcomes["Failed"] or outcomes["Inconclusive"]:
        problems.append(f"Failed: {outcomes['Failed']}; inconclusive: {outcomes['Inconclusive']}")
    if outcomes.keys() - {"Passed", "Skipped", "Failed", "Inconclusive"}:
        problems.append(f"Unexpected NUnit outcomes: {dict(outcomes)}")
    if not outcomes["Passed"] and not outcomes["Failed"] and any(case["state"] == "Runnable" for case in group["cases"]):
        problems.append("No runnable cases executed")
    covered = (actual == expected and status.get("group") == group["id"] and status.get("exit_code") in (0, 1)
               and (seed is None or status.get("seed") == seed) and (output / "results.trx").is_file())
    return problems, status, outcomes, dict(timings), covered


def build_problems(path):
    try:
        status = read_json(path)
    except (OSError, ValueError) as error:
        return [f"Final build report is invalid: {error}"]
    if status.get("ready") is True:
        return []
    stages = status.get("stages", {})
    problems = ["Build preparation did not complete: " + ", ".join(f"{name}={result}" for name, result in stages.items())]
    for stage, name in {"CI self-checks": "ci-tests.log", "Restore": "restore.log", "Compile": "build.log",
                        "Discovery": "plan.log", "Archive": "archive.log"}.items():
        if stages.get(stage) != "failure":
            continue
        log = path.parent / name
        if log.exists():
            lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
            errors = list(dict.fromkeys(line for line in lines if re.search(r"\berror\b|FAILED|Traceback", line, re.IGNORECASE)))
            problems.append(f"{name}:\n" + "\n".join(errors or lines[-40:]))
    return problems


def markdown_block(text):
    fence = "`" * max(3, max((len(match) + 1 for match in re.findall(r"`+", text)), default=0))
    return f"{fence}text\n{text}\n{fence}"


def report(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    (output / "result.json").unlink(missing_ok=True)
    args.timings.unlink(missing_ok=True)
    failures = build_problems(args.build_status) if args.build_status is not None else []
    if os.environ.get("CI_BUILD_READY", "true") != "true":
        failures.append("Build was not delivered to test runners; tests did not start.")
    build_ok = not failures
    for key in ("CI_BUILD_RESULT", "CI_TEST_RESULT"):
        value = os.environ.get(key)
        if value is not None and value != "success" and (key != "CI_TEST_RESULT" or build_ok):
            failures.append(f"GitHub job did not complete successfully: {key}={value}")
    try:
        plan = read_json(args.plan)
        validate_plan(plan)
    except (OSError, ValueError, KeyError) as error:
        if build_ok:
            failures.append(f"No valid test plan: {error}")
        plan = {"seed": "unknown", "groups": [], "excluded": [], "catalog": []}
    all_covered = build_ok and bool(plan["groups"])
    timings, rows = {}, []
    for group in plan["groups"] if build_ok else []:
        try:
            problems, status, outcomes, group_timings, covered = inspect_group(group, args.results / group["id"], seed=plan["seed"])
        except (OSError, ValueError, ET.ParseError) as error:
            problems, status, outcomes, group_timings, covered = [f"Invalid report: {error}"], {}, Counter(), {}, False
        timings.update(group_timings)
        all_covered &= covered
        if problems:
            failures.append(f"[{group['id']}]\n" + "\n\n".join(problems))
        rows.append({"group": group["id"], "passed": outcomes["Passed"], "failed": outcomes["Failed"],
                     "skipped": outcomes["Skipped"], "seconds": status.get("seconds", 0), "ok": not problems,
                     "rss": status.get("peak_sampled_tree_rss_bytes"), "available": status.get("min_available_memory_bytes")})
    failed = bool(failures) or not all_covered
    result = {"status": "failed" if failed else "passed", "all_covered": all_covered,
              "commit": os.environ.get("GITHUB_SHA", "local"), "run_id": os.environ.get("GITHUB_RUN_ID", "local"),
              "seed": plan["seed"], "expected": len(plan["catalog"]), "groups": rows}
    run_url = ""
    if os.environ.get("GITHUB_REPOSITORY") and os.environ.get("GITHUB_RUN_ID"):
        run_url = f"{os.environ.get('GITHUB_SERVER_URL', 'https://github.com')}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    mib = lambda value: f"{value / 1024**2:.0f} MiB" if value is not None else "n/a"
    resources = [f"{row['group']}: {row['seconds']:.1f}s; peak RAM {mib(row['rss'])}; min available {mib(row['available'])}" for row in rows]
    text = "\n".join([f"Build & Test Debug: {'FAILED' if failed else 'PASSED'}", f"Commit: {result['commit']}",
                      f"Run: {run_url or 'local'}", f"NUnit seed: {plan['seed']}",
                      f"Expected cases: {result['expected']}; Explicit excluded: {len(plan['excluded'])}", *resources, "",
                      "\n\n".join(failures) if failed else "All expected tests accounted for. No failures."]) + "\n"
    (output / "errors.txt").write_text(text, encoding="utf-8")
    summary = [f"## Build & Test Debug - {'FAILED' if failed else 'PASSED'}", "",
               "**Copy the failure block below.**" if failed else "All expected tests accounted for.", "",
               "| Group | Passed | Failed | Skipped | Time | Peak RAM | Min. available | Status |",
               "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"]
    for row in rows:
        summary.append(f"| {row['group']} | {row['passed']} | {row['failed']} | {row['skipped']} | {row['seconds']:.1f}s | "
                       f"{mib(row['rss'])} | {mib(row['available'])} | {'OK' if row['ok'] else 'FAIL'} |")
    display = text[:64000]
    if len(text) > len(display):
        display += "\n... Full details are in errors.txt."
    summary.extend(["", markdown_block(display)])
    rendered = "\n".join(summary) + "\n"
    (output / "summary.md").write_text(rendered, encoding="utf-8")
    write_json(output / "result.json", result)
    print(rendered)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(rendered)
    if all_covered:
        write_json(args.timings, timings)
    return 0 if args.defer_verdict else int(failed)


def verdict(args):
    try:
        result = read_json(args.output / "result.json")
    except (OSError, ValueError) as error:
        print(f"Final report was not generated: {error}", file=sys.stderr)
        return 1
    for field, variable in (("commit", "GITHUB_SHA"), ("run_id", "GITHUB_RUN_ID")):
        if os.environ.get(variable) and result.get(field) != os.environ[variable]:
            print("Final report belongs to a different workflow run.", file=sys.stderr)
            return 1
    print((args.output / "errors.txt").read_text(encoding="utf-8"))
    if os.environ.get("CI_REPORT_SAVED", "success") != "success":
        print("The test-results artifact was not saved. Check the upload step.", file=sys.stderr)
        return 1
    return int(result.get("status") != "passed" or result.get("all_covered") is not True)


def main():
    parser = argparse.ArgumentParser(description="Discover, divide and verify SS14 NUnit tests")
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    prepare_parser.add_argument("--seed", type=int)
    prepare_parser.add_argument("--output", type=Path, required=True)
    prepare_parser.add_argument("--timings", type=Path)
    prepare_parser.set_defaults(handler=prepare)
    build_parser = commands.add_parser("record-build")
    build_parser.add_argument("--output", type=Path, required=True)
    build_parser.set_defaults(handler=record_build)
    run_parser = commands.add_parser("run")
    run_parser.add_argument("--plan", type=Path, required=True)
    run_parser.add_argument("--group", required=True)
    run_parser.add_argument("--output", type=Path, required=True)
    run_parser.set_defaults(handler=run_group)
    report_parser = commands.add_parser("report")
    report_parser.add_argument("--plan", type=Path, required=True)
    report_parser.add_argument("--results", type=Path, required=True)
    report_parser.add_argument("--timings", type=Path, required=True)
    report_parser.add_argument("--build-status", type=Path)
    report_parser.add_argument("--output", type=Path, default=Path("test_results"))
    report_parser.add_argument("--defer-verdict", action="store_true")
    report_parser.set_defaults(handler=report)
    verdict_parser = commands.add_parser("verdict")
    verdict_parser.add_argument("--output", type=Path, required=True)
    verdict_parser.set_defaults(handler=verdict)
    args = parser.parse_args()
    try:
        return args.handler(args) or 0
    except (OSError, ValueError, StopIteration, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"Final report was not generated: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
