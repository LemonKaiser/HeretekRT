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


def discovered_cases(root, inherited="Runnable"):
    state = root.get("runstate", inherited)
    if inherited in ("Explicit", "Ignored", "NotRunnable"):
        state = inherited
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
    timings = read_json(args.timings) if args.timings and args.timings.exists() else {}
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
        buckets = split_classes(cases, 1 if assembly == "Content.Tests" else args.shards, timings)
        for index, classes in enumerate(buckets, 1):
            group_id = "unit" if assembly == "Content.Tests" else f"integration-{index}"
            plan["groups"].append({"id": group_id, "assembly": assembly, "sha256": assembly_hash(dll), "classes": classes,
                                   "cases": [case for case in cases if case["class"] in classes]})
        print(f"{assembly}: {len(cases)} cases, {len(buckets)} group(s)", flush=True)
    validate_plan(plan)
    write_json(output / "plan.json", plan)
    matrix = {"include": [{"group": group["id"]} for group in plan["groups"]]}
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.write(f"matrix={json.dumps(matrix)}\n")


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
    for key, value in {"Where": class_filter(group["classes"]), "RandomSeed": plan["seed"],
                       "ConsoleOut": 0, "MapWarningTo": "Failed", "WorkDirectory": output,
                       "TestOutputXml": output, "UseNUnitIdforTestCaseId": "true", "ExplicitMode": "None"}.items():
        ET.SubElement(nunit, key).text = str(value)
    settings_path = output / "tests.runsettings"
    ET.ElementTree(settings).write(settings_path, encoding="utf-8", xml_declaration=True)
    command = ["dotnet", "test", str(dll), "--settings", str(settings_path),
               "--results-directory", str(output), "--logger", "trx;LogFileName=results.trx",
               "--blame-crash-dump-type", "mini", "--blame-hang-timeout", "15m",
               "--blame-hang-dump-type", "mini"]
    environment = dict(os.environ, DOTNET_gcServer="1", DOTNET_CLI_UI_LANGUAGE="en")
    print(f"{group['id']}: {len(group['cases'])} cases; seed {plan['seed']}", flush=True)
    print(shlex.join(command), flush=True)
    started = time.monotonic()
    status = {"group": group["id"], "seed": plan["seed"], "exit_code": -1,
              "started_utc": datetime.now(timezone.utc).isoformat(), "command": command}
    try:
        verify_runtime(ROOT)
        if assembly_hash(dll) != group.get("sha256"):
            raise ValueError("Test assembly does not match the discovery plan; rebuild and prepare again")
        with (output / "console.log").open("w", encoding="utf-8") as log:
            with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT) as process:
                while True:
                    try:
                        status["exit_code"] = process.wait(timeout=60)
                        break
                    except subprocess.TimeoutExpired:
                        print(f"{group['id']}: running for {time.monotonic() - started:.0f}s", flush=True)
    except (OSError, ValueError) as error:
        status["error"] = str(error)
        raise
    finally:
        status["seconds"] = time.monotonic() - started
        status["finished_utc"] = datetime.now(timezone.utc).isoformat()
        if sys.platform == "linux":
            import resource
            usage = resource.getrusage(resource.RUSAGE_CHILDREN)
            status["peak_child_rss_bytes"] = usage.ru_maxrss * 1024
            status["cpu_seconds"] = usage.ru_utime + usage.ru_stime
        write_json(output / "execution.json", status)
    print(f"{group['id']}: exit {status['exit_code']}, {status['seconds']:.1f}s", flush=True)
    if status["exit_code"]:
        print((output / "console.log").read_text(encoding="utf-8", errors="replace")[-12000:])
    return 0 if status["exit_code"] == 0 else 1


def inspect_group(group, output):
    problems = []
    status_path = output / "execution.json"
    status = read_json(status_path) if status_path.exists() else {}
    if status.get("group") != group["id"] or status.get("exit_code") != 0:
        problems.append(f"Test process failed or did not finish (exit {status.get('exit_code', 'missing')})")
    if status.get("error"):
        problems.append(status["error"])
    if not (output / "results.trx").is_file():
        problems.append("Missing TRX report")
    xml_path = output / f"{group['assembly']}.xml"
    if not xml_path.exists():
        return problems + ["Missing NUnit XML report"], status, Counter(), {}
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
            problems.append(f"{case.get('fullname')}: {failure.findtext('message', '')[:1500]}")
    # OneTimeSetUp/OneTimeTearDown failures belong to suites, not necessarily a case.
    for suite in root.iter("test-suite"):
        failure = suite.find("failure")
        if failure is not None:
            problems.append(f"{suite.get('fullname')}: {failure.findtext('message', '')[:1500]}")
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
    return problems, status, outcomes, dict(timings)


def report(args):
    plan = read_json(args.plan)
    validate_plan(plan)
    lines = ["## Build & Test Debug", "", "| Group | Passed | Failed | Skipped | Time | Peak child RSS | Status |",
             "| --- | ---: | ---: | ---: | ---: | ---: | --- |"]
    failed = False
    timings = {}
    details = []
    for group in plan["groups"]:
        try:
            problems, status, outcomes, group_timings = inspect_group(group, args.results / group["id"])
        except (OSError, ValueError, ET.ParseError) as error:
            problems, status, outcomes, group_timings = [f"Invalid report: {error}"], {}, Counter(), {}
        timings.update(group_timings)
        failed |= bool(problems)
        memory = f"{status['peak_child_rss_bytes'] / 1024**2:.0f} MiB" if "peak_child_rss_bytes" in status else "n/a"
        lines.append(f"| {group['id']} | {outcomes['Passed']} | {outcomes['Failed']} | {outcomes['Skipped']} | "
                     f"{status.get('seconds', 0):.1f}s | {memory} | {'FAIL' if problems else 'OK'} |")
        if problems:
            details.extend(["", f"### {group['id']}", "", "```text", *problems[:10], "```"])
    lines.extend(["", f"NUnit seed: `{plan['seed']}`. Explicit cases excluded: {len(plan['excluded'])}.",
                  "Full logs, TRX, runsettings and crash/hang diagnostics are in the test artifacts.",
                  "To reproduce: build DebugOpt, then run the same plan/group with ci_tests.py run."])
    lines.extend(details)
    summary = "\n".join(lines) + "\n"
    print(summary)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary)
    write_json(args.timings, timings)
    return int(failed)


def main():
    parser = argparse.ArgumentParser(description="Discover, divide and verify SS14 NUnit tests")
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    prepare_parser.add_argument("--shards", type=int, default=2)
    prepare_parser.add_argument("--seed", type=int)
    prepare_parser.add_argument("--output", type=Path, required=True)
    prepare_parser.add_argument("--timings", type=Path)
    prepare_parser.set_defaults(handler=prepare)
    run_parser = commands.add_parser("run")
    run_parser.add_argument("--plan", type=Path, required=True)
    run_parser.add_argument("--group", required=True)
    run_parser.add_argument("--output", type=Path, required=True)
    run_parser.set_defaults(handler=run_group)
    report_parser = commands.add_parser("report")
    report_parser.add_argument("--plan", type=Path, required=True)
    report_parser.add_argument("--results", type=Path, required=True)
    report_parser.add_argument("--timings", type=Path, required=True)
    report_parser.set_defaults(handler=report)
    args = parser.parse_args()
    try:
        return args.handler(args) or 0
    except (OSError, ValueError, StopIteration, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"CI error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
