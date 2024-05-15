# Windows gRPC Documentation

### Building gRPC

1. Install VSCode
2. Install Git: `winget install --id Git.Git -e --source winget`
2. Install chocolatey:
    - In Powershell in admin mode:
        1. If `Get-ExecutionPolicy` returns `Restricted`, run `Set-ExecutionPolicy AllSigned`.
        2. Run `Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))`
3. Install CMake: `winget install kitware.cmake`
4. Install Active State Perl: Go to [this url](https://www.activestate.com/products/perl/) and download Perl. `choco install activeperl` didn't work for me. 
5. Install `Go`: `choco install golang`
6. Install `yasm` (and add to PATH): `choco install yasm`
7. Clone `git clone --recursive https://github.com/grpc/grpc`
8. Run the following:
```
cd grpc
cd cmake
md build
cd build
call "%VS140COMNTOOLS%..\..\VC\vcvarsall.bat" x64
cmake ..\.. -GNinja -DCMAKE_BUILD_TYPE=Release
cmake --build .
```


Install .NET SDK 8.0
`dotnet nuget add source --name nuget.org https://api.nuget.org/v3/index.json`
`dotnet add package Grpc.Core --version 2.46.6`