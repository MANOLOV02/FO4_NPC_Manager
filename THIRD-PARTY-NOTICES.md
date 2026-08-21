# Third-Party Notices

This product includes third-party material. Below is what is used, under which
licence, and what each licence requires.

The full text of every licence referenced here is distributed alongside this
file, in the `licenses/` folder. The programme's own licence is the GNU General
Public License version 3, in `LICENSE`.

## Components

Copyright lines are reproduced as they appear in the distributed binaries or in the upstream
licence file, which is what the MIT and Apache-2.0 licences require.

| Component | Version | Licence | Copyright / Author | Project |
|---|---|---|---|---|
| FO4 Base Library | 1.0.0 | GPL-3.0 | ManoloV02 | https://github.com/MANOLOV02/FO4_Base_Library |
| BSA/BA2 Library | 1.0.0 | GPL-3.0 | ManoloV02 | https://github.com/MANOLOV02/BSA_BA2_Library_DLL |
| DirectXTexWrapper | 1.0.0 | GPL-3.0 | ManoloV02 | https://github.com/MANOLOV02/DirectXTexWrapper |
| NiflySharp — **modified fork** | 1.1.0 | GPL-3.0 | ousnius | https://github.com/MANOLOV02/NiflySharpFork |
| HavokLib (ported decoder) | — | GPL-3.0 | Lukas Cone (PredatorCZ) | https://github.com/PredatorCZ/HavokLib |
| DirectXTex | — | MIT | © Microsoft Corporation | https://github.com/microsoft/DirectXTex |
| Material Editor (MaterialLib) | 1.0.0 | MIT | © 2018 ousnius | https://github.com/ousnius/Material-Editor |
| OpenTK.Core / .Graphics / .Mathematics / .Windowing.* | 4.9.3 | MIT | Copyright (c) 2006-2018 Stefanos Apostolopoulos (stapostol@gmail.com), for the Open Toolkit library | https://github.com/opentk/opentk |
| OpenTK.GLControl | 4.0.2 | MIT | Copyright (c) 2025 Team OpenTK | https://github.com/opentk/opentk |
| GLFW (`glfw3.dll`, via OpenTK.redist.glfw 3.3.8.39) | 3.3.8 | zlib/libpng | Copyright (c) 2002-2006 Marcus Geelnard; Copyright (c) 2006-2019 Camilla Löwy | https://www.glfw.org |
| SharpZipLib | 1.4.2 | MIT | Copyright © 2000-2022 SharpZipLib Contributors | https://github.com/icsharpcode/SharpZipLib |
| K4os.Compression.LZ4 / .Streams | 1.3.8 | MIT | Copyright (c) 2017 Milosz Krajewski | https://github.com/MiloszKrajewski/K4os.Compression.LZ4 |
| K4os.Hash.xxHash | 1.0.8 | MIT | Copyright (c) 2017 Milosz Krajewski | https://github.com/MiloszKrajewski/K4os.Hash.xxHash |
| miniball | 1.0.4 | Apache-2.0 | Lorenzo Delana (this fork); original by Martin Kutz (FU Berlin), Kaspar Fischer (ETH Zurich) and Bernd Gärtner (ETH Zurich) | https://github.com/SearchAThing-forks/miniball |
| System.IO.Pipelines | 6.0.3 | MIT | © Microsoft Corporation. All rights reserved. | https://github.com/dotnet/runtime |
| Ijwhost (`Ijwhost.dll`) | — | MIT | © Microsoft Corporation. All rights reserved. | https://github.com/dotnet/runtime |
| xEdit (format declarations) | — | MPL-2.0 | ElminsterAU and the xEdit contributors | https://github.com/TES5Edit/TES5Edit |

### Modified components (GPL-3.0 §5a)

**NiflySharp is modified.** The `NiflySharp.dll` distributed here is *not* the upstream build: it is
built from https://github.com/MANOLOV02/NiflySharpFork, which carries changes on top of
ousnius/NiflySharp. The Corresponding Source for that binary is the fork, not the upstream project.
Upstream remains at https://github.com/ousnius/NiflySharp.

No other third-party component is modified. In particular, `miniball.dll` is redistributed exactly
as published on NuGet, which is what Apache-2.0 §4(b) asks to be stated.

### Licence texts included

| File | Applies to |
|---|---|
| `LICENSE` | this programme, and every component above marked GPL-3.0 |
| `licenses/MIT.txt` | every component marked MIT |
| `licenses/Apache-2.0.txt` | miniball |
| `licenses/MPL-2.0.txt` | xEdit and the files derived from it |
| `licenses/GLFW-zlib.txt` | GLFW (verbatim copy of its own notice, which carries its copyright lines) |

The zlib/libpng licence requires its notice only in **source** distributions; GLFW's notice is
included here regardless.

## Source code availability

This programme is licensed under the GNU General Public License version 3. GPL-3.0 §6 requires the
Corresponding Source to be made available to anyone who receives the object code. MPL-2.0 §3.2
requires the same for the files derived from xEdit.

**How this is met — GPL-3.0 §6(d).** This programme is conveyed by offering access for download.
§6(d) allows the Corresponding Source to live on a different server, "provided you maintain clear
directions next to the object code saying where to find the Corresponding Source". This file travels
inside the package, next to the binaries, and those directions are the table below:

| Component | Repository |
|---|---|
| FO4 Base Library | https://github.com/MANOLOV02/FO4_Base_Library |
| BSA/BA2 Library | https://github.com/MANOLOV02/BSA_BA2_Library_DLL |
| FO4 NPC Manager | https://github.com/MANOLOV02/FO4_NPC_Manager |
| Wardrobe Manager | https://github.com/MANOLOV02/Wardrobe_Manager |
| Nif Explorer | https://github.com/MANOLOV02/NifExplorer |
| BA2/BSA Manager | https://github.com/MANOLOV02/Ba2_Bsa_Manager |
| DirectXTexWrapper | https://github.com/MANOLOV02/DirectXTexWrapper |
| NiflySharp (the modified fork actually used) | https://github.com/MANOLOV02/NiflySharpFork |

**Identifying the revision for a given binary.** The repositories above hold the full history. To be
given the exact revision the binary you hold was built from, open an issue on the corresponding
repository stating the version shown in the application, and it will be pointed out to you at no
charge. The same applies to the MPL-2.0 covered files listed in the xEdit section below.

This document does not claim any other means of obtaining the source, and makes no promise about
release tagging.

## xEdit (FO4Edit / TES5Edit / SSEEdit) — MPL-2.0 in detail

- **Licence:** Mozilla Public License, version 2.0 (MPL-2.0)
- **Licence text:** `licenses/MPL-2.0.txt` (verbatim copy)
- **Official copy:** https://mozilla.org/MPL/2.0/
- **Upstream project:** https://github.com/TES5Edit/TES5Edit
- **Authors:** ElminsterAU and the xEdit contributors

### What is used, and how

xEdit's format declarations — `Core/wbDefinitionsFO4.pas` and
`Core/wbDefinitionsTES5.pas` — describe the binary structure of Fallout 4 and
Skyrim plugin files.

The generator `Tools/CanonLayoutGen/emit.py` **reads those declarations and
mechanically translates them** into the VB.NET schema tables this application uses
to read and write plugins. Everything that generator emits, and everything
generated in turn from that, is a **derivative work** of MPL-2.0 material:

    FO4_Base_Library/ESP/Canon/Generated/WbSchemaGen_FO4.vb
    FO4_Base_Library/ESP/Canon/Generated/WbSchemaGen_TES5.vb
    FO4_Base_Library/ESP/Canon/Generated/WbFormatters_FO4.vb
    FO4_Base_Library/ESP/Canon/Generated/WbFormatters_TES5.vb
    FO4_Base_Library/ESP/Canon/Generated/WbConditions_FO4.vb
    FO4_Base_Library/ESP/Canon/Generated/WbConditions_TES5.vb
    FO4_Base_Library/ESP/Canon/Generated/WbRecords.vb
    FO4_Base_Library/ESP/Canon/Generated/WbViews_FO4.vb
    FO4_Base_Library/ESP/Canon/Generated/WbViews_TES5.vb
    FO4_Base_Library/ESP/Canon/Generated/WbViews_Interfaces.vb

The following hand-written files transcribe decision logic, type ordinals, wire-format constants or
the declaration DSL itself from the same source, and are covered on the same basis:

    FO4_Base_Library/ESP/Canon/WbDecidersImpl.vb
    FO4_Base_Library/ESP/Canon/WbDecidersImpl2.vb
    FO4_Base_Library/ESP/Canon/WbDecidersCond.vb
    FO4_Base_Library/ESP/Canon/WbCommon.vb
    FO4_Base_Library/ESP/Canon/WbCore.vb
    FO4_Base_Library/ESP/Canon/WbDsl.vb
    FO4_Base_Library/ESP/Canon/WbValueDefs.vb
    FO4_Base_Library/ESP/Canon/WbMemberDefs.vb
    FO4_Base_Library/ESP/Canon/WbReader.vb
    FO4_Base_Library/ESP/Canon/WbRecursive.vb

Each of those files carries the MPL-2.0 notice in its header.

The generator itself, `Tools/CanonLayoutGen/`, embeds the parameter order of the xEdit declaration
functions and the member names of its type enumerations, so it is covered too. Its Source Code Form
is available through the channel described under *Source code availability*.

### What MPL-2.0 requires

It is **per-file** copyleft, not per-project:

- **§3.1 and §3.2** — when distributing the programme, the Source Code Form of
  the covered files must be made available under MPL-2.0, and recipients must be
  told how to obtain it. See *Source code availability* above.
- **§3.3** — the rest of the programme may carry any licence. The copyleft does
  **not** spread to files that do not derive from covered material. This is what
  allows the programme as a whole to be distributed under GPL-3.0.
- **§3.4** — licence and copyright notices on covered material may not be removed
  or altered.

What the licence does **not** cover: the facts of the format itself. That a field
occupies two bytes at a given offset is a fact about the game's files, not
anyone's creation. What *is* covered is the expression of those declarations and,
therefore, their mechanical translation.

### How compliance is met here

1. The full licence text is distributed in `licenses/MPL-2.0.txt`.
2. Every derived file carries its MPL-2.0 notice in its header.
3. This document identifies the origin, the authors, and where to get the
   upstream project.
4. The Source Code Form of the covered files in `FO4_Base_Library/ESP/Canon/` is published at
   https://github.com/MANOLOV02/FO4_Base_Library. The generator is available through the channel
   described under *Source code availability* above.
