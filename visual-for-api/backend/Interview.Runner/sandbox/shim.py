"""Доверенный диспетчер внутри одноразового контейнера. Shell не используется."""
import json
import math
import os
import pathlib
import selectors
import shutil
import signal
import subprocess
import sys
import time

OUTPUT_LIMIT = 32768


def emit(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def capture(command, timeout, input_file=None, env=None):
    """Ограничивает время и объём stdout/stderr, уничтожает группу дочерних процессов."""
    started = time.monotonic()
    with open(input_file or "/dev/null", "rb") as stream:
        process = subprocess.Popen(command, stdin=stream, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, start_new_session=True, env=env)
        selector = selectors.DefaultSelector()
        selector.register(process.stdout, selectors.EVENT_READ)
        output = bytearray()
        timed_out = False
        exceeded_output = False
        peak_memory = 0
        try:
            while selector.get_map():
                if time.monotonic() - started > timeout:
                    timed_out = True
                    break
                try:
                    status = pathlib.Path(f"/proc/{process.pid}/status").read_text()
                    memory = next((int(line.split()[1]) for line in status.splitlines() if line.startswith("VmRSS:")), 0)
                    peak_memory = max(peak_memory, memory)
                except (FileNotFoundError, ProcessLookupError):
                    pass
                for key, _ in selector.select(0.02):
                    chunk = os.read(key.fileobj.fileno(), 4096)
                    if not chunk:
                        selector.unregister(key.fileobj)
                        continue
                    output.extend(chunk)
                    if len(output) > OUTPUT_LIMIT:
                        exceeded_output = True
                        break
                if exceeded_output:
                    break
            if timed_out or exceeded_output:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=1)
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            selector.close()
            process.stdout.close()
        return {"output": output[:OUTPUT_LIMIT].decode("utf-8", errors="replace"),
                "exitCode": process.returncode, "timedOut": timed_out,
                "durationMs": int((time.monotonic() - started) * 1000),
                "memoryKb": peak_memory,
                "errorKind": "timeout" if timed_out else "output" if exceeded_output else "resource" if process.returncode in (-9, 137) else "runtime" if process.returncode else None}


def main():
    def watchdog(_signum, _frame):
        raise TimeoutError("Истёк срок служебного протокола")
    signal.signal(signal.SIGALRM, watchdog)
    signal.alarm(45)
    job = json.loads(sys.stdin.readline())
    language = job["language"]
    commands = {
        "Python": ("main.py", None, ["python3", "-I", "main.py"]),
        "JavaScript": ("main.js", None, ["node", "main.js"]),
        "TypeScript": ("main.ts", ["node", "/opt/node-global/typescript/bin/tsc", "main.ts", "--target", "ES2022", "--module", "commonjs", "--moduleResolution", "node", "--esModuleInterop", "--typeRoots", "/opt/node-global/@types", "--types", "node", "--outDir", "out"], ["node", "out/main.js"]),
        "C++": ("main.cpp", ["g++", "-std=c++20", "-O1", "main.cpp", "-o", "solution"], ["./solution"]),
        "C#": ("Program.cs", ["dotnet", "build", "solution.csproj", "--nologo", "-o", "out", "-p:UseSharedCompilation=false", "-nodeReuse:false", "-m:1"], ["dotnet", "out/solution.dll"]),
        "Go": ("main.go", ["go", "build", "-p", "1", "-o", "solution", "main.go"], ["./solution"]),
        "Java": ("Main.java", ["javac", "Main.java"], ["java", "-XX:ActiveProcessorCount=1", "-XX:MaxRAMPercentage=60", "Main"]),
    }
    filename, compile_command, run_command = commands[language]
    pathlib.Path(filename).write_text(job["code"], encoding="utf-8")
    pathlib.Path("NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    if language == "C#":
        pathlib.Path("solution.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
    env = os.environ.copy()
    pathlib.Path("/work/tmp").mkdir(exist_ok=True)
    env.update({"GOCACHE": "/work/gocache", "GOPATH": "/work/go", "GOPROXY": "off", "GOSUMDB": "off", "CGO_ENABLED": "0", "TMPDIR": "/work/tmp"})
    if compile_command:
        compiled = capture(compile_command, 30, env=env)
        if compiled["exitCode"] != 0 or compiled["timedOut"] or compiled["errorKind"]:
            compiled["errorKind"] = "compile"
            emit({"phase": "result", **compiled})
            return
    # Освобождаем временные файлы компилятора до уменьшения лимита памяти.
    for directory in ["gocache", "go", "obj", "tmp"]:
        shutil.rmtree(directory, ignore_errors=True)
    emit({"phase": "compiled"})
    signal.alarm(10)
    if sys.stdin.readline().strip() != "run":
        return
    pathlib.Path("stdin.txt").write_text(job["input"], encoding="utf-8")
    signal.alarm(math.ceil(job["timeLimitMs"] / 1000) + 3)
    env["DOTNET_GCHeapHardLimitPercent"] = "3C"
    env["NODE_OPTIONS"] = f"--max-old-space-size={max(16, int(job['memoryLimitMb'] * 0.6))}"
    result = capture(run_command, job["timeLimitMs"] / 1000, "stdin.txt", env)
    emit({"phase": "result", **result})


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        emit({"phase": "result", "output": "Ошибка окружения sandbox: " + type(error).__name__, "exitCode": -1,
              "timedOut": False, "durationMs": 0, "memoryKb": 0, "errorKind": "resource"})
