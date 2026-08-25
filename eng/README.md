# Engineering

Repository-local build, packaging, validation, and release automation belongs here.

`Run-NativeAotPrototype.ps1` builds and publishes the x64 Native AOT configuration, then launches its output as a loose development package using the generated manifest. This folder-mode launch is needed because project-mode `RunPackagedApp` in Windows App Development CLI 0.6.1 expects the managed `apphost.exe`, which a Native AOT publish does not produce.
