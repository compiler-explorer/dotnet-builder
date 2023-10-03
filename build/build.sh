#!/bin/bash

set -exu

VERSION=$1
OS=linux
TIMESTAMP="$(date +%Y%m%d)"
AOT_BUILD_NEEDED=0

if echo "${VERSION}" | grep -q 'trunk'; then
    VERSION=trunk-"$TIMESTAMP"
    BRANCH=main
    VERSION_WITHOUT_V="${VERSION}"
    AOT_BUILD_NEEDED=1
else
    BRANCH="${VERSION}"
    VERSION_WITHOUT_V="${VERSION:1}"
    if [[ "${VERSION:1:1}" -lt 8 ]]; then OS=Linux; fi
    if [[ "${VERSION:1:1}" -ge 7 ]]; then AOT_BUILD_NEEDED=1; fi
fi

URL=https://github.com/dotnet/runtime.git

FULLNAME=dotnet-${VERSION}.tar.xz
OUTPUT=$2/${FULLNAME}

DOTNET_REVISION=$(git ls-remote --heads ${URL} refs/heads/"${BRANCH}" | cut -f 1)
REVISION="dotnet-${DOTNET_REVISION}"
LAST_REVISION="${3:-}"

echo "ce-build-revision:${REVISION}"
echo "ce-build-output:${OUTPUT}"

if [[ "${REVISION}" == "${LAST_REVISION}" ]]; then
    echo "ce-build-status:SKIPPED"
    exit
fi

DIR=$(pwd)/dotnet/runtime

git clone --depth 1 -b "${BRANCH}" ${URL} "${DIR}"
cd "${DIR}"

commit="$(git rev-parse HEAD)"
echo "HEAD is at: $commit"

CORE_ROOT="$(pwd)"/artifacts/tests/coreclr/"${OS}".x64.Release/Tests/Core_Root

# Build everything in Release mode
if [[ "$AOT_BUILD_NEEDED" -eq 1 ]]; then
  ./build.sh Clr+Clr.Aot+Libs -c Release --ninja -ci -p:OfficialBuildId="$TIMESTAMP"-99
else
  ./build.sh Clr+Libs -c Release --ninja -ci -p:OfficialBuildId="$TIMESTAMP"-99
fi

# Build Checked JIT compilers (only Checked JITs are able to filter assembly for printing codegen)
./build.sh Clr.AllJits -c Checked --ninja

cd src/tests

# Generate CORE_ROOT for Release
./build.sh Release generatelayoutonly
cd ../..

echo "${VERSION_WITHOUT_V}+${commit}" > "${CORE_ROOT}"/version.txt

# Copy Checked JITs to CORE_ROOT
cp artifacts/bin/coreclr/"${OS}".x64.Checked/libclrjit*.so "${CORE_ROOT}"
cp artifacts/bin/coreclr/"${OS}".x64.Checked/libclrjit*.so "${CORE_ROOT}"/crossgen2

if [[ "$AOT_BUILD_NEEDED" -eq 1 ]]; then
  # For .NET 7 and above, copy nativeaot files at CORE_ROOT/aot
  # later we can use `dotnet publish -p:PublishAot=true --packages "${CORE_ROOT}"/aot`
  ./dotnet.sh build -c Release -p:ContinuousIntegrationBuild=true -p:OfficialBuildId="$TIMESTAMP"-99 \
      src/installer/pkg/projects/nativeaot-packages.proj

  mkdir "${CORE_ROOT}"/aot
  if [[ "$BRANCH" == "main" ]]; then
    ILC="$(find artifacts/bin -type f -name ilc | head -1)"
    PACKAGE_VERSION="$("${ILC}" --version)"
    PACKAGE_VERSION="${PACKAGE_VERSION%-*}-*"
  else
    PACKAGE_VERSION="$VERSION_WITHOUT_V"
  fi

  cp artifacts/packages/Release/Shipping/*ILCompiler*.nupkg "${CORE_ROOT}"/aot

  # initialize AOT packages directory
  pushd /tmp

  "${DIR}/dotnet.sh" new console -o app
  "${DIR}/dotnet.sh" add app package "Microsoft.DotNet.ILCompiler" --version "$PACKAGE_VERSION" --package-directory "${CORE_ROOT}"/aot -s "${CORE_ROOT}"/aot
  "${DIR}/dotnet.sh" add app package "runtime.linux-x64.microsoft.dotnet.ilcompiler" --version "$PACKAGE_VERSION" --package-directory "${CORE_ROOT}"/aot -s "${CORE_ROOT}"/aot
  "${DIR}/dotnet.sh" publish -p:PublishAot=true --packages "${CORE_ROOT}"/aot app

  ILC="$(find "${CORE_ROOT}"/aot -type f -name ilc | head -1)"
  PACKAGE_VERSION="$("${ILC}" --version)"
  echo "$PACKAGE_VERSION" > "${CORE_ROOT}"/aot/package-version.txt

  popd
fi

cd "${DIR}"

# Build DisassemblyLoader
./.dotnet/dotnet build -c Release /root/DisassemblyLoader/DisassemblyLoader.csproj -o "${CORE_ROOT}"/DisassemblyLoader

# Copy the bootstrapping .NET SDK, needed for 'dotnet build'
# Exclude the pdbs as when they are present, when running on Linux we get:
# Error: Image is either too small or contains an invalid byte offset or count.
# System.BadImageFormatException: Image is either too small or contains an invalid byte offset or count.
mv .dotnet/ "${CORE_ROOT}"/
cd "${CORE_ROOT}"/..
XZ_OPT=-2 tar Jcf "${OUTPUT}" --exclude \*.pdb --transform "s,^./,./dotnet-${VERSION}/," -C Core_Root .

echo "ce-build-status:OK"
