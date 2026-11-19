#!/usr/bin/env python3
"""Builds and runs the lift harness with the Unity editor's own compiler and .NET runtime (no mono needed).
   python3 Tools/LiftHarness/run.py 1 3000        seeds 1..3000
   python3 Tools/LiftHarness/run.py 4242          one mountain, listed
   UNITY_APP=/path/to/Unity.app overrides where the editor is looked for."""
import glob, json, os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
APP = os.environ.get("UNITY_APP") or sorted(glob.glob("/Applications/Unity/Hub/Editor/*/Unity.app"))[-1]
SCRIPTING = os.path.join(APP, "Contents/Resources/Scripting")
DOTNET = os.path.join(SCRIPTING, "NetCoreRuntime/dotnet")
FW = sorted(glob.glob(os.path.join(SCRIPTING, "NetCoreRuntime/shared/Microsoft.NETCore.App/*")))[-1]
OUT = os.path.join(tempfile.gettempdir(), "summit-lift-harness")
os.makedirs(OUT, exist_ok=True)
S = os.path.join(ROOT, "Assets/Summit/Scripts")
src = glob.glob(os.path.join(HERE, "*.cs")) + [os.path.join(S, f + ".cs") for f in
      ("Slope", "Course", "Ridge", "Cliffs", "Deadly", "LiftLine", "LiftNetwork", "LiftRamp", "CableTune")]
refs = ["-r:" + f for f in glob.glob(os.path.join(FW, "System*.dll")) + [os.path.join(FW, "netstandard.dll"), os.path.join(FW, "mscorlib.dll")]]
dll = os.path.join(OUT, "lift.dll")
r = subprocess.run([DOTNET, os.path.join(SCRIPTING, "DotNetSdkRoslyn/csc.dll"), "-nologo", "-langversion:latest", "-optimize+",
                    "-nowarn:CS0162,CS0219,CS0649,CS0414", "-out:" + dll] + refs + src)
if r.returncode:
    sys.exit(r.returncode)
with open(os.path.join(OUT, "lift.runtimeconfig.json"), "w") as f:
    json.dump({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": os.path.basename(FW)}}}, f)
sys.exit(subprocess.run([DOTNET, dll] + sys.argv[1:]).returncode)
