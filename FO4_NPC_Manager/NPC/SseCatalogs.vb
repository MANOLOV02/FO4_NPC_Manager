Imports System.IO
Imports FO4_Base_Library

''' <summary>
''' Catalogs for the Skyrim (RaceMenu/skee64) editors: every list the user picks from is DERIVED from real
''' data — the installed <c>skee64.ini</c>, the merged loose+BSA file dictionary, the plugins, or the NPC's
''' own skeleton. Nothing here is typed by hand and nothing is hardcoded that the game reads from disk.
'''
''' Why this exists: RaceMenu itself has no catalogs in C++. Its menu either lists records the game already
''' loaded (HDPT, RACE tint masks) or runs a plain recursive directory scan through the generic
''' <c>GetExternalFiles</c> Scaleform hook (ScaleformCharGenFunctions.cpp:1497-1589). Our
''' <see cref="FilesDictionary_class"/> is a strict superset of that scan because it also sees inside BSAs.
''' </summary>
Friend Module SseCatalogs

    ''' <summary>The four overlay zones skee64 instantiates on an actor. Hair is declared in
    ''' OverlayInterface.h but Skyrim SE never reads a count for it (main.cpp:774-781 covers Body/Hands/
    ''' Feet/Face only), so it is deliberately absent — offering it would author nodes the engine never
    ''' creates.</summary>
    Friend Enum OverlayZone
        Body = 0
        Hands = 1
        Feet = 2
        Face = 3
    End Enum

    ''' <summary>skee64 hardcoded fallbacks when the ini is missing or the key is absent (main.cpp:120-127).</summary>
    Private ReadOnly DefaultOverlayCounts As Integer() = {3, 3, 3, 3}

    Private ReadOnly IniSectionByZone As String() = {"Overlays/Body", "Overlays/Hands", "Overlays/Feet", "Overlays/Face"}
    Private ReadOnly NodePrefixByZone As String() = {"Body", "Hands", "Feet", "Face"}

    ''' <summary>Root under which RaceMenu keeps overlay textures. Taken from the shipped RaceMenu UI asset
    ''' (the string <c>textures\actors\character\overlays</c> is embedded in RaceMenu.bsa) and corroborated by
    ''' <c>skee64.ini</c>'s <c>sDefaultTexture=textures\actors\character\overlays\default.dds</c>. Real mods
    ''' nest below it (e.g. <c>…\Overlays\Skin Features\Freckles\Body\x.dds</c>), so the scan is recursive.</summary>
    Friend Const OverlayTextureRoot As String = "Textures\Actors\Character\Overlays\"

    Private ReadOnly _lock As New Object()
    Private _overlayCounts As Integer()

    ''' <summary>Number of overlay slots the engine will instantiate for <paramref name="zone"/>, i.e. the
    ''' valid range of <c>[Ovl{n}]</c> indices is <c>0 .. count-1</c>. Read once from
    ''' <c>Data\SKSE\Plugins\skee64.ini</c> (then <c>skee64_custom.ini</c>, which overrides it — main.cpp:239,249).
    ''' Authoring an overlay past this bound produces a node skee64 never creates: the preset would look
    ''' right here and do nothing in-game.</summary>
    Friend Function OverlayCount(zone As OverlayZone) As Integer
        EnsureOverlayCounts()
        Return _overlayCounts(CInt(zone))
    End Function

    Private Sub EnsureOverlayCounts()
        SyncLock _lock
            If _overlayCounts IsNot Nothing Then Return
            Dim counts = DirectCast(DefaultOverlayCounts.Clone(), Integer())
            For Each iniName In {"skee64.ini", "skee64_custom.ini"}
                Dim iniPath = Path.Combine(Config_App.Current.DataPath, "SKSE", "Plugins", iniName)
                If Not File.Exists(iniPath) Then Continue For
                Try
                    ReadOverlayCountsFromIni(iniPath, counts)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[SSE-CATALOG] could not read {iniName}: {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
            ' skee64 clamps the count (main.cpp:810-828); a negative or absurd value would otherwise
            ' size the editor's slot grid.
            For i = 0 To counts.Length - 1
                counts(i) = Math.Max(0, Math.Min(counts(i), &H7F))
            Next
            _overlayCounts = counts
        End SyncLock
    End Sub

    ''' <summary>Minimal INI section reader for the <c>[Overlays/*] iNumOverlays</c> keys. Deliberately not a
    ''' general INI parser — it only needs the four counts, and skee64's own reader is equally forgiving
    ''' (values may carry a trailing <c>; comment</c>).</summary>
    Private Sub ReadOverlayCountsFromIni(iniPath As String, counts As Integer())
        Dim section As String = ""
        For Each rawLine In File.ReadLines(iniPath)
            Dim line = rawLine.Trim()
            If line.Length = 0 OrElse line.StartsWith(";") Then Continue For
            If line.StartsWith("[") AndAlso line.EndsWith("]") Then
                section = line.Substring(1, line.Length - 2).Trim()
                Continue For
            End If
            Dim eq = line.IndexOf("="c)
            If eq <= 0 Then Continue For
            Dim key = line.Substring(0, eq).Trim()
            If Not key.Equals("iNumOverlays", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim value = line.Substring(eq + 1).Trim()
            Dim semi = value.IndexOf(";"c)
            If semi >= 0 Then value = value.Substring(0, semi).Trim()
            Dim n As Integer
            If Not Integer.TryParse(value, n) Then Continue For
            For z = 0 To IniSectionByZone.Length - 1
                If section.Equals(IniSectionByZone(z), StringComparison.OrdinalIgnoreCase) Then counts(z) = n
            Next
        Next
    End Sub

    ''' <summary>The skee64 node name for a slot, e.g. <c>Body [Ovl0]</c> (OverlayInterface.h:33-46). This is
    ''' the identity the render, the preset and the engine all key on.</summary>
    Friend Function OverlayNodeName(zone As OverlayZone, index As Integer) As String
        Return $"{NodePrefixByZone(CInt(zone))} [Ovl{index}]"
    End Function

    ''' <summary>Zone of an existing node name, or Nothing when the node is not an overlay node we author
    ''' (spell overlays <c>[SOvl{n}]</c> and any other NiOverride node are round-tripped, never edited).</summary>
    Friend Function ZoneOfNode(nodeName As String) As OverlayZone?
        If String.IsNullOrEmpty(nodeName) Then Return Nothing
        For z = 0 To NodePrefixByZone.Length - 1
            If nodeName.StartsWith(NodePrefixByZone(z) & " [Ovl", StringComparison.OrdinalIgnoreCase) Then Return CType(z, OverlayZone)
        Next
        Return Nothing
    End Function

    ''' <summary>Index parsed out of <c>… [Ovl{n}]</c>, or -1.</summary>
    Friend Function IndexOfNode(nodeName As String) As Integer
        If String.IsNullOrEmpty(nodeName) Then Return -1
        Dim open = nodeName.IndexOf("[Ovl", StringComparison.OrdinalIgnoreCase)
        If open < 0 Then Return -1
        Dim close = nodeName.IndexOf("]"c, open)
        If close < 0 Then Return -1
        Dim digits = nodeName.Substring(open + 4, close - open - 4)
        Dim n As Integer
        Return If(Integer.TryParse(digits, n), n, -1)
    End Function

    ''' <summary>Every <c>.dds</c> under <see cref="OverlayTextureRoot"/>, loose and inside BSAs, as full
    ''' dictionary keys. Empty when nothing is installed there.</summary>
    Friend Function OverlayTextureKeys() As List(Of String)
        Try
            Return FilesDictionary_class.GetFilteredKeys(OverlayTextureRoot, {".dds"})
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SSE-CATALOG] overlay texture scan failed: {ex.GetType().Name}: {ex.Message}")
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>Show the shared in-archive texture picker rooted at the overlay folder and return the chosen
    ''' path in the form a <c>.jslot</c> stores it (relative to <c>Textures\</c>, no prefix). Nothing when the
    ''' user cancels. <paramref name="currentJslotPath"/> preselects the current entry.
    '''
    ''' This replaces an <c>OpenFileDialog</c>: a file dialog cannot see textures inside a BSA, and it hands
    ''' back an absolute disk path which is a dead path for the engine.</summary>
    Friend Function PickOverlayTexture(owner As IWin32Window, currentJslotPath As String) As String
        Dim keys = OverlayTextureKeys()
        If keys.Count = 0 Then
            MessageBox.Show(owner,
                "No overlay textures are installed." & vbCrLf & vbCrLf &
                "RaceMenu reads them from " & OverlayTextureRoot & " (loose files or inside a BSA). " &
                "Install an overlay/tattoo mod and reload.",
                "No overlay textures found", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return Nothing
        End If
        Return PickTexture(owner, currentJslotPath, keys, OverlayTextureRoot)
    End Function

    ''' <summary>Skin-override textures replace the actor's body/hand/feet diffuse, so they are ordinary
    ''' character textures and are picked from the whole <c>Textures\</c> tree rather than the overlay folder.</summary>
    Friend Function PickSkinTexture(owner As IWin32Window, currentJslotPath As String) As String
        Dim cfg = FilesDictionary_class.TexturesDictionary_Filter
        Dim keys As List(Of String)
        Try
            keys = FilesDictionary_class.GetFilteredKeys(cfg)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SSE-CATALOG] texture scan failed: {ex.GetType().Name}: {ex.Message}")
            Return Nothing
        End Try
        If keys Is Nothing OrElse keys.Count = 0 Then Return Nothing
        Return PickTexture(owner, currentJslotPath, keys, cfg.RootPrefix)
    End Function

    ''' <summary>Shared body of the texture pickers: show the in-archive tree rooted at
    ''' <paramref name="rootPrefix"/>, preselect the current entry, and return the chosen path in the form a
    ''' <c>.jslot</c> stores it (relative to <c>Textures\</c>, no prefix). Nothing when cancelled.</summary>
    Private Function PickTexture(owner As IWin32Window, currentJslotPath As String,
                                 keys As List(Of String), rootPrefix As String) As String
        ' The picker preselects by full dictionary key; the preset stores the path without the Textures\ root.
        Dim initialKey As String = ""
        If Not String.IsNullOrWhiteSpace(currentJslotPath) Then
            initialKey = FO4UnifiedMaterial_Class.CorrectTexturePath(currentJslotPath)
        End If

        Using dlg As New DictionaryFilePicker_Form(keys, rootPrefix,
                                                   FilesDictionary_class.TexturesDictionary_Filter.AllowedExtensions,
                                                   initialKey)
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Dim key = dlg.DictionaryPicker_Control1.SelectedKey
            If String.IsNullOrWhiteSpace(key) Then Return Nothing
            Return RaceMenuJslot.ToGameTexturePath(key)
        End Using
    End Function

    ' --- RaceMenu PAINT lists (bug #0) ---------------------------------------------------------------------
    ' RaceMenu presents warpaints and body/hand/feet/face paints as NAMED lists it accumulates from every mod's
    ' Add*Paint Papyrus registrations — never a file browser. RaceMenuPaintCatalog reconstructs those same lists
    ' from the installed scripts; these helpers show them with the PaintListPicker (name shown, path stored).

    Friend Enum PaintPickKind
        Cancel = 0
        Clear = 1
        Pick = 2
    End Enum

    Friend Structure PaintPickResult
        Public Kind As PaintPickKind
        Public Entry As RaceMenuPaintCatalog.Entry
    End Structure

    ''' <summary>The paint category for an overlay zone. Warpaint is a separate (face tint-mask) list and is not
    ''' reachable from a zone.</summary>
    Friend Function PaintCategoryForZone(zone As OverlayZone) As RaceMenuPaintCatalog.PaintCategory
        Select Case zone
            Case OverlayZone.Body : Return RaceMenuPaintCatalog.PaintCategory.Body
            Case OverlayZone.Hands : Return RaceMenuPaintCatalog.PaintCategory.Hands
            Case OverlayZone.Feet : Return RaceMenuPaintCatalog.PaintCategory.Feet
            Case Else : Return RaceMenuPaintCatalog.PaintCategory.Face
        End Select
    End Function

    ''' <summary>The friendly RaceMenu paint name registered for <paramref name="path"/> in the paint list of
    ''' <paramref name="zone"/>, or Nothing when no installed mod registered that exact texture. A <c>.jslot</c>
    ''' overlay stores ONLY the texture path — RaceMenu never persists the display name — so the name is re-derived
    ''' here by matching the stored path against the catalog the Add*Paint scripts built. The match is
    ''' prefix/slash/case-insensitive because the registration path and the stored path may differ in the leading
    ''' <c>textures\</c> and separator style.</summary>
    Friend Function PaintNameForPath(zone As OverlayZone, path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return Nothing
        Dim catalog = RaceMenuPaintCatalog.Current
        If catalog Is Nothing Then Return Nothing
        Dim want = NormalizePaintPath(path)
        For Each e In catalog.Entries(PaintCategoryForZone(zone))
            If NormalizePaintPath(e.Path) = want Then Return e.DisplayName
        Next
        Return Nothing
    End Function

    Private Function NormalizePaintPath(p As String) As String
        Dim s = If(p, "").Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If s.StartsWith("textures\") Then s = s.Substring("textures\".Length)
        Return s
    End Function

    Private Function PaintCategoryLabel(cat As RaceMenuPaintCatalog.PaintCategory) As String
        Select Case cat
            Case RaceMenuPaintCatalog.PaintCategory.Warpaint : Return "warpaint"
            Case RaceMenuPaintCatalog.PaintCategory.Body : Return "body paint"
            Case RaceMenuPaintCatalog.PaintCategory.Hands : Return "hand paint"
            Case RaceMenuPaintCatalog.PaintCategory.Feet : Return "feet paint"
            Case Else : Return "face paint"
        End Select
    End Function

    ''' <summary>Show the RaceMenu named list for <paramref name="cat"/> and return the user's choice. This is the
    ''' replacement for the loose+BSA file browser: RaceMenu offers only what a mod registered, by name.
    ''' <paramref name="allowNone"/> adds a "(None — clear)" row. Returns Cancel when the list is empty or the user
    ''' backs out.</summary>
    Friend Function PickPaint(owner As IWin32Window, cat As RaceMenuPaintCatalog.PaintCategory,
                              currentPath As String, allowNone As Boolean) As PaintPickResult
        Dim catalog = RaceMenuPaintCatalog.Current
        If catalog Is Nothing OrElse catalog.CountFor(cat) = 0 Then
            MessageBox.Show(owner,
                $"No {PaintCategoryLabel(cat)} entries are registered." & vbCrLf & vbCrLf &
                "RaceMenu builds this list at runtime from the Add*Paint calls in installed mods' Papyrus scripts " &
                "(loose or inside a BSA) — there is no file browser and no static folder. Install a mod that " &
                "registers " & PaintCategoryLabel(cat) & " and reload.",
                "No " & PaintCategoryLabel(cat) & " found", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return New PaintPickResult With {.Kind = PaintPickKind.Cancel}
        End If
        Dim title = "Choose " & PaintCategoryLabel(cat) & $" ({catalog.CountFor(cat)} available)"
        Using dlg As New PaintListPicker_Form(title, catalog.Entries(cat), currentPath, allowNone)
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return New PaintPickResult With {.Kind = PaintPickKind.Cancel}
            If dlg.ChosenEntry.HasValue Then
                Return New PaintPickResult With {.Kind = PaintPickKind.Pick, .Entry = dlg.ChosenEntry.Value}
            End If
            Return New PaintPickResult With {.Kind = PaintPickKind.Clear}
        End Using
    End Function

    ''' <summary>Is a paint/overlay texture present in the load order (loose + BSA)? Uses the SAME normalisation the
    ''' material loader and the renderer's skip check use (lowercase, backslashes, prepend <c>textures\</c>), so
    ''' "resolves" here matches what the renderer can actually load — a mod may register a paint whose <c>.dds</c> it
    ''' does not ship (e.g. CommunityOverlays registers <c>…\27 Head.dds</c> but only ships <c>27 Head M.dds</c>),
    ''' and the editor should show that as missing rather than silently render nothing.</summary>
    Friend Function TextureResolves(gameRelPath As String) As Boolean
        If String.IsNullOrWhiteSpace(gameRelPath) Then Return False
        Dim key = gameRelPath.Replace("/"c, "\"c).ToLowerInvariant()
        If Not key.StartsWith("textures\") Then key = "textures\" & key
        Return FilesDictionary_class.Dictionary.ContainsKey(key)
    End Function

    ''' <summary>Colour for a missing-texture row — the same red the tint tab uses for a missing mask.</summary>
    Friend ReadOnly MissingTextureColor As System.Drawing.Color = System.Drawing.Color.FromArgb(200, 40, 40)

    ''' <summary>RaceMenu's BUILT-IN body-scale node sliders, as (friendly label, skeleton node). This is NOT
    ''' invented: it is the exact set RaceMenu's own <c>RaceMenuPlugin.psc</c> registers (recovered by decompiling
    ''' <c>RaceMenuPlugin.pex</c> inside <c>RaceMenu.bsa</c> — the <c>$labels</c> and <c>NINODE_*</c> node literals
    ''' it binds each slider to via <c>NiOverride.AddNodeTransformScale</c>). RaceMenu has NO skeleton scan — the UI
    ''' list is exactly what plugins register (PapyrusNiOverride.cpp:1381 enumerates only already-registered nodes).
    ''' Other mods (XPMSE) register MORE through the same mechanism; <see cref="RaceMenuNodeCatalog"/> picks those up
    ''' dynamically from the installed scripts, and the editor also unions any node a loaded preset carries.</summary>
    Friend ReadOnly RaceMenuBaseBodyNodes As (Label As String, Node As String)() = {
        ("Height", "NPC"),
        ("Head", "NPC Head [Head]"),
        ("Breast L", "NPC L Breast"), ("Breast R", "NPC R Breast"),
        ("Breast Curve L", "NPC L Breast01"), ("Breast Curve R", "NPC R Breast01"),
        ("Glute L", "NPC L Butt"), ("Glute R", "NPC R Butt"),
        ("Biceps L", "NPC L UpperarmTwist1 [LUt1]"), ("Biceps R", "NPC R UpperarmTwist1 [RUt1]"),
        ("Biceps 2 L", "NPC L UpperarmTwist2 [LUt2]"), ("Biceps 2 R", "NPC R UpperarmTwist2 [RUt2]")
    }

    ''' <summary>RaceMenu's built-in WEAPON-scale node sliders (same <c>RaceMenuPlugin.psc</c> registration). They
    ''' scale the equipped weapon/shield/quiver, which the NPC-appearance preview does not render, so the editor
    ''' surfaces them only under the "show all" toggle rather than in the default body view.</summary>
    Friend ReadOnly RaceMenuBaseWeaponNodes As (Label As String, Node As String)() = {
        ("Weapon", "WEAPON"), ("Sword", "WeaponSword"), ("Axe", "WeaponAxe"), ("Mace", "WeaponMace"),
        ("Bow", "WeaponBow"), ("Weapon Back", "WeaponBack"), ("Shield", "SHIELD"), ("Quiver", "QUIVER")
    }

End Module
