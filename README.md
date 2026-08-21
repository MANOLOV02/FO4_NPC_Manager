# FO4 NPC Manager

## Licence

This project is licensed under the **GNU General Public License version 3**.
The full text is in `LICENSE`. Credits are in `LICENSE_CREDITS.txt`, and the
per-component copyright lines, licence texts and source-code offer are in
`THIRD-PARTY-NOTICES.md` and the `licenses/` folder.

## Requires the following libraries/packages

 - ManoloV02: FO4 Base Library - Licensed under the GPL-3.0 License (https://github.com/MANOLOV02/FO4_Base_Library)
 - ManoloV02: BSA/BA2 Library - Licensed under the GPL-3.0 License (https://github.com/MANOLOV02/BSA_BA2_Library_DLL)
 - ManoloV02: DirectXTexWrapper - Licensed under the GPL-3.0 License (https://github.com/MANOLOV02/DirectXTexWrapper)
 - Ousnius: NiflySharp - Licensed under the GPL-3.0 License (https://github.com/MANOLOV02/NiflySharpFork)
     MODIFICADO. Fork de https://github.com/ousnius/NiflySharp con cambios propios; el fuente correspondiente es el del fork
 - Lukas Cone: HavokLib - Licensed under the GPL-3.0 License (https://github.com/PredatorCZ/HavokLib)
     hkaLosslessCompressedAnimation decoder ported into FO4_Base_Library/HkxLosslessAnimationGraphParser.vb
 - Microsoft: DirectXTex - Licensed under the MIT License (https://github.com/microsoft/DirectXTex)
     wrapped by DirectXTexWrapper
 - Ousnius: Material Editor - Licensed under the MIT License (https://github.com/ousnius/Material-Editor)
 - Stefanos Apostolopoulos: OpenTK (Core, Graphics, Mathematics, Windowing) - Licensed under the MIT License (https://github.com/opentk/opentk)
     Copyright (c) 2006-2018 Stefanos Apostolopoulos, for the Open Toolkit library
 - Team OpenTK: OpenTK.GLControl - Licensed under the MIT License (https://github.com/opentk/opentk)
     Copyright (c) 2025 Team OpenTK
 - Marcus Geelnard, Camilla Löwy: GLFW (glfw3.dll) - Licensed under the zlib/libpng License (https://www.glfw.org)
     redistributed through OpenTK.redist.glfw
 - SharpZipLib Contributors: SharpZipLib 1.4.2 - Licensed under the MIT License (https://github.com/icsharpcode/SharpZipLib)
 - Milosz Krajewski: K4os.Compression.LZ4 (+ .Streams) - Licensed under the MIT License (https://github.com/MiloszKrajewski/K4os.Compression.LZ4)
     Copyright (c) 2017 Milosz Krajewski
 - Milosz Krajewski: K4os.Hash.xxHash - Licensed under the MIT License (https://github.com/MiloszKrajewski/K4os.Hash.xxHash)
     Copyright (c) 2017 Milosz Krajewski
 - Lorenzo Delana: miniball - Licensed under the Apache-2.0 License (https://github.com/SearchAThing-forks/miniball)
     fork of https://github.com/hbf/miniball by Martin Kutz (FU Berlin), Kaspar Fischer (ETH Zurich) and Bernd Gaertner (ETH Zurich); reached through NiflySharp
 - Microsoft: System.IO.Pipelines - Licensed under the MIT License (https://github.com/dotnet/runtime)
 - Microsoft: Ijwhost (Ijwhost.dll) - Licensed under the MIT License (https://github.com/dotnet/runtime)
     C++/CLI host shim required by DirectXTexWrapper
 - ElminsterAU and the xEdit contributors: xEdit - Licensed under the MPL-2.0 License (https://github.com/TES5Edit/TES5Edit)
     the plugin format declarations are mechanically translated into the schema tables; see THIRD-PARTY-NOTICES.md

## Build

Build with MSBuild, configuration `Publish`:

```
msbuild FO4_NPC_Manager/NPC_Manager_FO4.vbproj -t:Restore,Build -p:Configuration=Publish -p:Platform=x64
```
