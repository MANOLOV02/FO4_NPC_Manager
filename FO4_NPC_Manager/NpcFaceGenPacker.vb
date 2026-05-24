Option Strict On
Imports System.IO
Imports BSA_BA2_Library_DLL.BethesdaArchive.Core
Imports FO4_Base_Library

''' <summary>
''' Packs the four FaceGen loose files baked by <c>FaceGenBuilder.BuildCharGen</c>
''' (1 NIF + 3 DDS) into the BA2 archive set anchored to the Save ESP plugin chosen
''' by the user. The plugin already exists at this point (just written by
''' SaveNpcEspWriter), so <see cref="ArchivePackager"/> reuses it as Slot 1 and the
''' PluginWriter callback never fires.
'''
''' Pack semantics are merge / upsert ([ArchivePackager.vb:599-602, :712-714]):
'''   - Existing entries in the BA2 not present in our 4-entry bundle are preserved
'''     verbatim via stream-copy from .bak.
'''   - Re-saving the same NPC: the 4 paths overlap; ComputeDiff CRC32 decides whether
'''     to stream-copy unchanged or rewrite changed entries.
''' Adding new NPCs grows the archive set; never destroys what was already there.
'''
''' After a successful pack the four loose files under Data\Meshes\... and
''' Data\Textures\... are deleted, mirroring how WM_PackUnpack handles the cloned
''' material flow — EXCEPT when packing a DebugMode bake (debugSandbox = True). There the bake
''' wrote the loose with a _2 suffix and the user keeps them on disk for inspection: the packer
''' reads the _2 files but names the BA2 entries canonically (the emitted NIF already embeds
''' canonical texture paths via FaceGenBuilder slotPlan.CanonSuffix), so the archive is identical
''' to a release pack, and the _2 loose are left untouched.
''' </summary>
Friend Module NpcFaceGenPacker

    ''' <summary>Result returned by <see cref="PackForNpc"/>.</summary>
    Friend Class PackResult
        Public Property Success As Boolean
        ''' <summary>Archive paths that were (re)written. Empty when bundle was unchanged.</summary>
        Public ReadOnly WrittenArchives As New List(Of String)
        ''' <summary>Archive paths skipped because the bundle was byte-identical.</summary>
        Public ReadOnly SkippedArchives As New List(Of String)
        ''' <summary>Loose files removed from disk after a successful pack.</summary>
        Public ReadOnly DeletedLoose As New List(Of String)
        ''' <summary>Now-empty directories pruned (up to, but excluding, Data) after the loose were removed.</summary>
        Public ReadOnly RemovedDirs As New List(Of String)
        ''' <summary>Free-form failure message when Success = False.</summary>
        Public Property ErrorMessage As String = ""
    End Class

    ''' <summary>Progress phases reported through the optional <paramref name="progress"/> callback.</summary>
    Friend Enum PackPhase
        BuildingBundle
        WritingArchive
        DeletingLoose
        Done
    End Enum

    ''' <summary>Lightweight progress envelope.</summary>
    Friend Class PackProgress
        Public Phase As PackPhase
        Public Detail As String = ""
        Public Current As Integer
        Public Max As Integer
    End Class

    ''' <summary>Pack the four bake outputs of <paramref name="originPlugin"/>:<paramref name="formIdLow"/>
    ''' into the BA2 archive set anchored to <paramref name="anchorPluginPath"/>.
    '''
    ''' Loose paths consumed (must already exist on disk; produced by FaceGenBuilder):
    '''   Data\Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;originPlugin&gt;\&lt;FormID8hex&gt;.nif
    '''   Data\Textures\Actors\Character\FaceCustomization\&lt;originPlugin&gt;\&lt;FormID8hex&gt;_d.dds
    '''   Data\Textures\Actors\Character\FaceCustomization\&lt;originPlugin&gt;\&lt;FormID8hex&gt;_msn.dds
    '''   Data\Textures\Actors\Character\FaceCustomization\&lt;originPlugin&gt;\&lt;FormID8hex&gt;_s.dds
    ''' </summary>
    ''' <param name="anchorPluginPath">Full path to the Save ESP plugin written this same
    ''' transaction. Its file name (without extension) becomes the BA2 ModBaseName, so the
    ''' engine auto-loads "&lt;name&gt; - Main.ba2" + "&lt;name&gt; - Textures.ba2" alongside the plugin.</param>
    ''' <param name="dataDir">FO4 Data folder (where the loose lives and the BA2 will be written).</param>
    ''' <param name="game">Game variant. Only Fallout4 is exercised here; Skyrim path is provided for parity.</param>
    ''' <param name="originPlugin">Plugin name segment in the FaceGen path (the NPC's source master,
    ''' e.g. "Fallout4.esm" or whichever auto-gen plugin owns the override). FaceGenBuilder built
    ''' the loose under this same segment.</param>
    ''' <param name="formIdLow">NPC FormID with the master-index high byte cleared (the same value
    ''' FaceGenBuilder used to name the files).</param>
    ''' <param name="debugSandbox">True when the bake ran with FaceGenBuilder.DebugMode on: the
    ''' loose were written with a _2 suffix. The packer then reads the _2 files, names the BA2
    ''' entries canonically, and skips deleting the loose (the _2 stay on disk). False = release:
    ''' reads the canonical loose and deletes them after packing.</param>
    ''' <param name="progress">Optional progress callback; invoked synchronously from the calling
    ''' (UI) thread.</param>
    Friend Function PackForNpc(anchorPluginPath As String,
                               dataDir As String,
                               game As Config_App.Game_Enum,
                               originPlugin As String,
                               formIdLow As UInteger,
                               ba2Version As UInteger,
                               debugSandbox As Boolean,
                               Optional progress As Action(Of PackProgress) = Nothing) As PackResult

        Dim result As New PackResult()

        If String.IsNullOrEmpty(anchorPluginPath) OrElse Not File.Exists(anchorPluginPath) Then
            result.ErrorMessage = $"Anchor plugin not found: '{anchorPluginPath}'."
            Return result
        End If
        If String.IsNullOrEmpty(dataDir) OrElse Not Directory.Exists(dataDir) Then
            result.ErrorMessage = $"Data folder not found: '{dataDir}'."
            Return result
        End If
        If String.IsNullOrEmpty(originPlugin) Then
            result.ErrorMessage = "Origin plugin name is empty."
            Return result
        End If

        ' Resolve the four loose paths produced by FaceGenBuilder. Order matters for the
        ' progress bar: NIF first (fastest), then 3 DDS.
        '
        ' Source vs entry split: in DebugMode (debugSandbox) the bake wrote the loose with a _2
        ' suffix and we read those, but the BA2 entry names are always canonical (non-_2) — the
        ' emitted NIF already embeds canonical texture paths, so the packed archive is identical
        ' to a release pack. In release the two coincide.
        Dim formIdHex = formIdLow.ToString("X8")
        Dim faceGeomDir = Path.Combine(dataDir,
            "Meshes", "Actors", "Character", "FaceGenData", "FaceGeom", originPlugin)
        Dim ddsBase = Path.Combine(dataDir,
            "Textures", "Actors", "Character", "FaceCustomization", originPlugin)

        Dim nifSuffix = If(debugSandbox, "_2.nif", ".nif")
        Dim dSuffix = If(debugSandbox, "_d_2.dds", "_d.dds")
        Dim nSuffix = If(debugSandbox, "_msn_2.dds", "_msn.dds")
        Dim sSuffix = If(debugSandbox, "_s_2.dds", "_s.dds")

        ' Sources read from disk.
        Dim nifSrc = Path.Combine(faceGeomDir, formIdHex & nifSuffix)
        Dim ddsDSrc = Path.Combine(ddsBase, formIdHex & dSuffix)
        Dim ddsNSrc = Path.Combine(ddsBase, formIdHex & nSuffix)
        Dim ddsSSrc = Path.Combine(ddsBase, formIdHex & sSuffix)

        ' Canonical names the entries take inside the BA2 (always non-_2).
        Dim nifEntry = Path.Combine(faceGeomDir, formIdHex & ".nif")
        Dim ddsDEntry = Path.Combine(ddsBase, formIdHex & "_d.dds")
        Dim ddsNEntry = Path.Combine(ddsBase, formIdHex & "_msn.dds")
        Dim ddsSEntry = Path.Combine(ddsBase, formIdHex & "_s.dds")

        Dim sources As String() = {nifSrc, ddsDSrc, ddsNSrc, ddsSSrc}
        For Each s In sources
            If Not File.Exists(s) Then
                result.ErrorMessage = $"Bake output missing: '{s}'. CharGen build did not produce all four files."
                Return result
            End If
        Next

        ' --- Phase 1: build VirtualEntry list (parses DDS headers, compresses payloads) ---
        Report(progress, PackPhase.BuildingBundle, "Compressing FaceGen NIF…", 0, 4)
        Dim entries As New List(Of VirtualEntry)
        Try
            entries.Add(MakeMaterialEntry(dataDir, nifSrc, nifEntry, game))
            Report(progress, PackPhase.BuildingBundle, "Compressing FaceCustomization _d.dds…", 1, 4)
            entries.Add(MakeTextureEntry(dataDir, ddsDSrc, ddsDEntry, game))
            Report(progress, PackPhase.BuildingBundle, "Compressing FaceCustomization _msn.dds…", 2, 4)
            entries.Add(MakeTextureEntry(dataDir, ddsNSrc, ddsNEntry, game))
            Report(progress, PackPhase.BuildingBundle, "Compressing FaceCustomization _s.dds…", 3, 4)
            entries.Add(MakeTextureEntry(dataDir, ddsSSrc, ddsSEntry, game))
            Report(progress, PackPhase.BuildingBundle, "Done compressing.", 4, 4)
        Catch ex As Exception
            result.ErrorMessage = $"Failed to build BA2 entries: {ex.GetType().Name}: {ex.Message}"
            Return result
        End Try

        ' --- Phase 2: hand the bundle to ArchivePackager ---
        Dim modBaseName = Path.GetFileNameWithoutExtension(anchorPluginPath)

        ' If the engine session previously mounted these archives via FilesDictionary, the
        ' pooled FileStreams hold sharing-read locks that block File.Move/Delete inside the
        ' packager's rewrite path. WM_PackUnpack hits the same race and unregisters before
        ' the rewrite — same dance here. After pack we re-register so the freshly-written
        ' archives are resolvable for the rest of the session.
        Dim preSet = ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName)
        For Each archivePath In preSet.Archives
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch
            End Try
        Next

        Dim req As New PackagerRequest With {
            .Game = MapGame(game),
            .Ba2Version = ba2Version,
            .ModBaseName = modBaseName,
            .OutputDir = dataDir,
            .Entries = entries,
            .BundleAlreadyCompressed = True,
            .MaxArchiveBytes = 3L << 30,
            .Overflow = ArchiveOverflowPolicy.ThrowOnExceed,
            .PluginWriter = Sub(p As String, g As GameKind)
                                ' Anchor plugin already exists (Save ESP wrote it before we
                                ' got called) so this callback should never fire. If it does,
                                ' something split into a numbered slot — emit a dummy plugin
                                ' so the engine still has something to anchor the BA2 to,
                                ' matching WM_PackUnpack.
                                PluginWriter.WriteLightMasterDummy(p, MapGameBack(g), PluginWriter.NPC_MANAGER_AUTHOR_CNAM)
                            End Sub
        }

        Report(progress, PackPhase.WritingArchive, $"Writing {modBaseName} - Main.ba2 + Textures.ba2…", -1, -1)

        Dim packResult As PackagerResult
        Try
            packResult = ArchivePackager.Pack(req)
        Catch ex As Exception
            ' Re-register existing archives even on failure so the FilesDictionary stays
            ' consistent for the rest of the session.
            For Each archivePath In ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName).Archives
                Try
                    FilesDictionary_class.RegisterArchive(archivePath)
                Catch
                End Try
            Next
            result.ErrorMessage = $"BA2 packer failed: {ex.GetType().Name}: {ex.Message}"
            Return result
        End Try

        result.WrittenArchives.AddRange(packResult.Archives)
        result.SkippedArchives.AddRange(packResult.Skipped)

        ' Re-mount: every archive in the set, not just the touched ones (Skipped ones are
        ' still on disk and we Unregistered them upstream).
        For Each archivePath In ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName).Archives
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch
            End Try
            FilesDictionary_class.RegisterArchive(archivePath)
        Next

        ' --- Phase 3: delete loose, ONLY if at least one archive received the bundle ---
        ' If the packer reported zero rewrites AND zero skips, the bundle wasn't actually
        ' committed anywhere — leave the loose alone so the user can retry.
        If packResult.Archives.Count = 0 AndAlso packResult.Skipped.Count = 0 Then
            result.ErrorMessage = "Packer returned no archives written or skipped — bundle not committed."
            Return result
        End If

        ' DebugMode: the loose are the _2 sandbox files the user keeps on disk for inspection /
        ' diffing. The content is already committed to the BA2 (under canonical names) — skip the
        ' delete + dictionary eviction entirely so the _2 remain untouched.
        If debugSandbox Then
            result.Success = True
            Report(progress, PackPhase.Done, "Done (debug: _2 loose kept on disk).", 1, 1)
            Return result
        End If

        Report(progress, PackPhase.DeletingLoose, "Removing loose files…", 0, sources.Length)
        Dim deletedAt As Integer = 0
        For Each src In sources
            Try
                If File.Exists(src) Then
                    File.Delete(src)
                    result.DeletedLoose.Add(src)
                End If
                ' Mirror WM: drop the dictionary entry so subsequent GetBytes resolves to
                ' the BA2 instead of trying to open a now-deleted loose path.
                Dim relUnderData = Path.GetRelativePath(dataDir, src).Correct_Path_Separator
                FilesDictionary_class.RemoveDictionaryEntry(relUnderData)
            Catch
                ' Best-effort: if a loose deletion fails, the BA2 still has the content.
                ' The next pack will see the leftover loose as a new bundle entry and merge.
            End Try
            deletedAt += 1
            Report(progress, PackPhase.DeletingLoose, $"Removed {deletedAt}/{sources.Length}", deletedAt, sources.Length)
        Next

        ' Prune directories left empty by the deletions, walking up toward (but never past) dataDir.
        ' Each FaceGen bake writes into Meshes\...\FaceGeom\<plugin>\ and Textures\...\FaceCustomization\
        ' <plugin>\; once the loose are packed + removed those per-plugin folders are usually empty.
        ' Only the dirs we actually deleted from are candidates; the emptiness check stops at the first
        ' ancestor that still holds another NPC / mod content, so we never over-delete.
        Dim affectedDirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each src In result.DeletedLoose
            Dim d = Path.GetDirectoryName(src)
            If Not String.IsNullOrEmpty(d) Then affectedDirs.Add(d)
        Next
        For Each leaf In affectedDirs
            RemoveEmptyDirsUpTo(leaf, dataDir, result.RemovedDirs)
        Next

        result.Success = True
        Report(progress, PackPhase.Done, "Done.", 1, 1)
        Return result
    End Function

    ''' <summary>Delete <paramref name="leafDir"/> and each ancestor left empty, walking UP until
    ''' (and excluding) <paramref name="stopDir"/>. Stops at the first non-empty ancestor. Never
    ''' deletes stopDir itself nor anything outside it (the StartsWith guard keeps the walk strictly
    ''' under stopDir). Best-effort: any IO failure aborts the walk for that branch without throwing —
    ''' a leftover empty dir is harmless.</summary>
    Private Sub RemoveEmptyDirsUpTo(leafDir As String, stopDir As String, removed As List(Of String))
        Try
            Dim sep = Path.DirectorySeparatorChar
            Dim stopFull = Path.GetFullPath(stopDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            Dim current = Path.GetFullPath(leafDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            While True
                ' Boundary: stop at dataDir and never walk outside it.
                If String.Equals(current, stopFull, StringComparison.OrdinalIgnoreCase) Then Exit While
                If Not current.StartsWith(stopFull & sep, StringComparison.OrdinalIgnoreCase) Then Exit While

                If Directory.Exists(current) Then
                    ' Only remove when truly empty (no files AND no subdirs).
                    Dim hasAny As Boolean = False
                    For Each e In Directory.EnumerateFileSystemEntries(current)
                        hasAny = True
                        Exit For
                    Next
                    If hasAny Then Exit While   ' a real ancestor with other content — stop here
                    Directory.Delete(current, recursive:=False)
                    If removed IsNot Nothing Then removed.Add(current)
                End If
                ' Walk up (also when current was already gone via a sibling leaf's walk).
                Dim parent = Path.GetDirectoryName(current)
                If String.IsNullOrEmpty(parent) Then Exit While
                current = parent
            End While
        Catch
            ' Best-effort cleanup; leftover empty dirs are harmless.
        End Try
    End Sub

    ' ============================================================================
    ' Entry builders — mirror Wardrobe_Manager.WM_PackUnpack.MakeMaterialEntry /
    ' MakeTextureEntry so the produced VirtualEntries are bit-compatible with the
    ' shared ArchivePackager pipeline. Kept private here per
    ' feedback_always_correct_path_no_optional_debt: NPC_Manager is the only caller
    ' for now, so no library promotion. If a third caller appears we hoist this into
    ' Ba2_Bsa_Library.
    ' ============================================================================

    Private Function MakeMaterialEntry(dataDir As String, sourcePath As String, entryPath As String, game As Config_App.Game_Enum) As VirtualEntry
        ' entryPath = canonical path that names the BA2 entry; sourcePath = the file we read
        ' (carries the _2 suffix in DebugMode). They coincide in release.
        Dim relUnderData = Path.GetRelativePath(dataDir, entryPath).Correct_Path_Separator
        Dim bytes = File.ReadAllBytes(sourcePath)
        Dim relDir As String = "", relFile As String = ""
        PathUtil.SplitDirFile(relUnderData, relDir, relFile)
        Dim crc = Ba2WriterCommon.Crc32Bytes(bytes)

        Dim ve As New VirtualEntry With {
            .Directory = relDir,
            .FileName = relFile,
            .Crc32 = crc
        }

        If game = Config_App.Game_Enum.Skyrim Then
            Dim cp = PayloadCompressor.CompressForBsa(bytes, wantCompressed:=True)
            ve.PreCompressed = True
            ve.PreCompressedBytes = cp.Bytes
            ve.PreCompressedCompSize = cp.CompSize
            ve.PreCompressedDecompSize = cp.DecompSize
        Else
            Dim cp = PayloadCompressor.CompressForBa2Gnrl(bytes,
                version:=8UI,
                compressionFormat:=Ba2WriterCommon.CompressionFormat.Zip,
                preset:=Ba2WriterCommon.ZlibPreset.Default)
            ve.PreCompressed = True
            ve.PreCompressedBytes = cp.Bytes
            ve.PreCompressedCompSize = cp.CompSize
            ve.PreCompressedDecompSize = cp.DecompSize
        End If

        Return ve
    End Function

    Private Function MakeTextureEntry(dataDir As String, sourcePath As String, entryPath As String, game As Config_App.Game_Enum) As VirtualEntry
        ' entryPath = canonical path that names the BA2 entry; sourcePath = the file we read
        ' (carries the _2 suffix in DebugMode). They coincide in release.
        Dim relUnderData = Path.GetRelativePath(dataDir, entryPath).Correct_Path_Separator
        Dim bytes = File.ReadAllBytes(sourcePath)

        If game = Config_App.Game_Enum.Skyrim Then
            Dim relDir As String = "", relFile As String = ""
            PathUtil.SplitDirFile(relUnderData, relDir, relFile)
            Dim cp = PayloadCompressor.CompressForBsa(bytes, wantCompressed:=True)
            Return New VirtualEntry With {
                .Directory = relDir,
                .FileName = relFile,
                .Crc32 = Ba2WriterCommon.Crc32Bytes(bytes),
                .PreCompressed = True,
                .PreCompressedBytes = cp.Bytes,
                .PreCompressedCompSize = cp.CompSize,
                .PreCompressedDecompSize = cp.DecompSize
            }
        End If

        Dim ve = Dx10Importer.FromDdsBytes(bytes, relUnderData)
        Dim payload = If(ve.Data, Array.Empty(Of Byte)())
        ve.Crc32 = Ba2WriterCommon.Crc32Bytes(payload)
        Dim cpDx10 = PayloadCompressor.CompressForBa2Dx10(payload,
            version:=8UI,
            compressionFormat:=Ba2WriterCommon.CompressionFormat.Zip,
            preset:=Ba2WriterCommon.ZlibPreset.Default)
        ve.Data = Nothing
        ve.PreCompressed = True
        ve.PreCompressedBytes = cpDx10.Bytes
        ve.PreCompressedCompSize = cpDx10.CompSize
        ve.PreCompressedDecompSize = cpDx10.DecompSize
        Return ve
    End Function

    Private Function MapGame(g As Config_App.Game_Enum) As GameKind
        Select Case g
            Case Config_App.Game_Enum.Fallout4 : Return GameKind.FO4_BA2
            Case Config_App.Game_Enum.Skyrim : Return GameKind.SSE_BSA
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(g))
        End Select
    End Function

    Private Function MapGameBack(g As GameKind) As Config_App.Game_Enum
        Select Case g
            Case GameKind.FO4_BA2 : Return Config_App.Game_Enum.Fallout4
            Case GameKind.SSE_BSA : Return Config_App.Game_Enum.Skyrim
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(g))
        End Select
    End Function

    Private Sub Report(progress As Action(Of PackProgress),
                       phase As PackPhase, detail As String,
                       current As Integer, max As Integer)
        If progress Is Nothing Then Return
        progress(New PackProgress With {
            .Phase = phase,
            .Detail = detail,
            .Current = current,
            .Max = max
        })
    End Sub

End Module
