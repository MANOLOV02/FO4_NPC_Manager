Imports FO4_Base_Library

''' <summary>
''' Diálogo "CharGen Options": tamaño de textura por canal del bake FaceGen + formato del diffuse.
''' Persiste en Config_App (config.json). Lógica de tamaño:
'''   - All (uniform): los 3 canales usan el tamaño Diffuse; Normal/Specular deshabilitados y siguen a D.
'''   - Per layer: cada canal su propio tamaño; los 3 habilitados.
''' Tamaño por canal: Inherit (MIP0 nativo, sin downgrade) o 512/1024/2048/4096/8192.
''' Formato por canal (misma sincronía All/Per-layer que el tamaño): Diffuse BC3(default)/BC7/Uncompressed,
''' N/S BC5(default)/Uncompressed. En All, N/S siguen al Diffuse (Uncompressed si el Diffuse lo es, sino BC5).
''' Tilde "Generate TGA": escribe un TGA uncompressed al lado de cada .dds. Botón "Revert to default" del tab
''' Size = All + Inherit + BC3/BC5 + no TGA. Toda la UI vive en el .Designer.vb.
''' </summary>
Public Class CharGenOptionsForm

    ' Guard para no disparar la lógica de sync mientras se cargan los valores iniciales.
    Private _loading As Boolean = False

    Private Sub CharGenOptionsForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        _loading = True
        Dim c = Config_App.Current
        ComboDiffuse.SelectedIndex = ResToIndex(c.Setting_FaceGenDiffuseResolution)
        ComboNormal.SelectedIndex = ResToIndex(c.Setting_FaceGenNormalResolution)
        ComboSpecular.SelectedIndex = ResToIndex(c.Setting_FaceGenSpecularResolution)
        ' Compresiones PER-GAME (set del juego activo → sin leak al cambiar de juego). Specular = FO4-only (compartido).
        Dim isSseL = (c.Game = Config_App.Game_Enum.Skyrim)
        SetComboIndex(ComboFormat, CInt(If(isSseL, c.Setting_FaceGenDiffuseCompression_SSE, c.Setting_FaceGenDiffuseCompression)))  ' Bc3=0/Bc7=1/Uncompressed=2
        SetComboIndex(ComboFormatN, CInt(If(isSseL, c.Setting_FaceGenNormalCompression_SSE, c.Setting_FaceGenNormalCompression)))  ' Bc5=0/Unc=1/Bc7=2/Bc3=3
        SetComboIndex(ComboFormatS, CInt(c.Setting_FaceGenSpecularCompression))                                                    ' mismo enum que el N (4 valores)
        CheckGenerateTga.Checked = c.Setting_FaceGenGenerateTga
        ' --- Tab "Fixes": NPC-only toggle, lives in NPC_Config (not Config_App). ---
        CheckBoxApplyGhoulHeadRearFix.Checked = NPC_Config.Current.ApplyGhoulHeadRearFix
        ' Eyebrows fixed-color override: gate persistido en Config_App (lo lee la librería en
        ' BuildSyntheticEyebrowLut). Requiere ESTE toggle + el archivo SkipEyebrowsTone.ini. Default True.
        CheckBoxApplyEyebrowsFixedColor.Checked = c.Setting_ApplyEyebrowsFixedColor
        ' Mouth vanilla fix gate → Config_App (lo lee la librería ChargenMouthFix en los parse-sites de
        ' render/bake). Aplica SOLO a BaseFemaleHeadChargen.tri. Default False = vanilla puro.
        CheckBoxApplyMouthVanillaFix.Checked = c.Setting_ApplyMouthVanillaFix
        ' Los 3 fixes de este tab son FO4-only. Deshabilitar (sin tocar el valor persistido, que se
        ' round-trip-ea intacto en OK) cuando el juego activo no es Fallout4. El gate REAL vive también
        ' en la app (ChargenMouthFix.IsActiveFor / IsGhoulHeadRearCase / BuildSyntheticEyebrowLut); esto
        ' es la señal visual coherente en la UI.
        Dim isFo4 = (c.Game = Config_App.Game_Enum.Fallout4)
        CheckBoxApplyGhoulHeadRearFix.Enabled = isFo4
        CheckBoxApplyEyebrowsFixedColor.Enabled = isFo4
        CheckBoxApplyMouthVanillaFix.Enabled = isFo4
        ' SSE-only fix: bake RaceMenu face overlays into the per-NPC diffuse. Enabled solo en Skyrim (inverso
        ' a los 3 de arriba); el valor persistido se round-trip-ea intacto cuando el juego activo es FO4.
        CheckBoxBakeSseRaceMenuOverlays.Checked = c.Setting_BakeSseRaceMenuOverlays
        CheckBoxBakeSseRaceMenuOverlays.Enabled = Not isFo4
        ' SSE-only fix: redirect head race/chargen/mesh .tri to High Poly Head when the record points at a missing
        ' or wrong-topology tri and the head is an exact HPH head (3832 F / 3598 M). Default OFF (opt-in). Enabled
        ' solo en Skyrim; el valor persistido se round-trip-ea intacto cuando el juego activo es FO4. Lo consume
        ' NpcMorphResolver.LoadTriForShape (render/preview) vía Config_App.Setting_SseResolveHighPolyHeadTri.
        CheckBoxResolveHphHeadTri.Checked = c.Setting_SseResolveHighPolyHeadTri
        ' FO4-only, DEFAULT ON: replicar la normalizacion de pesos de skin del MOTOR (w3 = 1−Σ, se descarta si ≤0).
        ' ⚠️ Replica un DEFECTO del CK, no una ley del motor: sirve para igualar al CK byte a byte, no para
        ' que la malla salga "mejor". Default False ⇒ camino normalizado de siempre, bit-idéntico.
        ' Fuente/VAs del RE en FO4_Base_Library.EngineSkinWeightNormalization. Enabled solo en FO4 porque el mecanismo NO
        ' está verificado en los binarios de Skyrim (el valor persistido se round-trip-ea intacto en SSE).
        CheckBoxReplicateEngineSkinNorm.Checked = NPC_Config.Current.ReplicateEngineSkinWeightNormalization
        CheckBoxReplicateEngineSkinNorm.Enabled = isFo4
        CheckBoxResolveHphHeadTri.Enabled = Not isFo4
        If c.Setting_FaceGenPerLayerResolution Then
            RadioPerLayer.Checked = True
        Else
            RadioAll.Checked = True
        End If

        ' --- Tab "FaceTint Conventions": cargar la convención concreta por bucket (índice = valor del enum). ---
        LoadConvention(ActiveConventionSettings(c))
        ' Blend es read-only: el combo deshabilitado muestra el valor fijo (record / Replace) del Designer.
        ComboDBlend.SelectedIndex = 0
        ComboNBlend.SelectedIndex = 0
        ComboSBlend.SelectedIndex = 0

        ' --- Tab "Tint Order": orden de composición configurable. ---
        InitSortUi(c)

        ApplyGameAwareUi(c.Game = Config_App.Game_Enum.Skyrim)

        _loading = False
        UpdateEnabledState()
    End Sub

    ''' <summary>Ajusta la UI de "FaceTint Conventions" al juego activo: en SSE los buckets Normal/Specular y Swap
    ''' son FO4-only (se ocultan) y el grupo Diffuse se retitula "Diffuse (SSE facegen-tint)". El per-blend-op (DWsByOp)
    ''' queda visible (afecta el compose SSE). Sin header ni reubicación (FO4 tampoco lo tiene) ⇒ el layout entra sin
    ''' cortes. Idempotente (se llama en Load).</summary>
    Private Sub ApplyGameAwareUi(isSse As Boolean)
        ' Buckets Normal/Specular y Swap: FO4-only (el normal SSE se compone por otro algoritmo, no por convención;
        ' no hay specular SSE; Swap es exclusivo FO4). El per-blend-op (DWsByOp) SÍ afecta el compose SSE
        ' (ResolveConvention lee DiffuseWorkingSpaceByBlend) → EDITABLE en ambos juegos (default = ley RaceMenu). Se
        ' muestra en su POSICIÓN del Designer (8,338; bottom 422 < 430, entra). NO reubicamos nada = sin corte.
        GroupConvNormal.Visible = Not isSse
        GroupConvSwap.Visible = Not isSse
        GroupConvDWsByOp.Visible = True

        GroupConvDiffuse.Text = If(isSse, "Diffuse (SSE facegen-tint)", "Diffuse")

        ' Tab "Texture Size": el default del formato NORMAL difiere por juego → tag "(default)" GAME-AWARE en ComboFormatN
        ' (FO4=BC5 idx0 tangent-space 2-ch; SSE=Uncompressed idx1: el _msn es MODEL-SPACE 3-ch y cualquier BCn destruye la
        ' dirección de la normal — MEDIDO: BC3 da RMS 5.07/255 y max 148/255 vs el vanilla, Uncompressed da 0.000 = exacto).
        ' El enable/disable + derivación All-vs-per-layer de N/S lo maneja UpdateEnabledState (corre DESPUÉS). Acá labels + tag.
        LabelNormal.Enabled = True
        LabelSpecular.Enabled = Not isSse
        Dim selN = ComboFormatN.SelectedIndex
        ComboFormatN.Items.Clear()
        ComboFormatN.Items.AddRange(If(isSse,
            New Object() {"BC5", "Uncompressed (default)", "BC7", "BC3"},
            New Object() {"BC5 (default)", "Uncompressed", "BC7", "BC3"}))
        If selN >= 0 AndAlso selN < ComboFormatN.Items.Count Then ComboFormatN.SelectedIndex = selN
    End Sub

    ' === Tab "Tint Order" ============================================================
    ' Copias en memoria de las reglas; se escriben a Config_App.Setting_FaceTintSort en OK.
    Private _tintRules As New List(Of FaceTintSortRule)
    Private _swapRules As New List(Of FaceTintSortRule)
    ' Valor de enum por item del combo (item i -> _xKeyValues(i)). Soporta huecos en el enum.
    Private _tintKeyValues As New List(Of Integer)
    Private _swapKeyValues As New List(Of Integer)

    ''' <summary>Puebla los combos de claves (nombres de enum, índice = valor) y carga las reglas + el
    ''' SkinTonePlacement guardados en una copia editable.</summary>
    ''' <summary>El tab "Tint Order" es GAME-AWARE: en SSE ordena las capas de tint del RACE (FaceTintSseTintSortKey)
    ''' y los overlays Face[Ovl] (FaceTintSseOverlaySortKey, en la lista "swap"), leyendo/guardando Setting_FaceTintSort_SSE;
    ''' en FO4 usa las claves y el set de FO4. Sets SEPARADOS ⇒ no se pisan. Default de cada juego via NewSortDefaults().</summary>
    Private _sortIsSse As Boolean
    Private Function SortTintEnumType() As Type
        Return If(_sortIsSse, GetType(FaceTintSseTintSortKey), GetType(FaceTintSortKey))
    End Function
    Private Function SortSwapEnumType() As Type
        Return If(_sortIsSse, GetType(FaceTintSseOverlaySortKey), GetType(FaceTintSwapSortKey))
    End Function
    Private Function ActiveSortSettings(c As Config_App) As FaceTintSortSettings
        If c.Game = Config_App.Game_Enum.Skyrim Then
            If c.Setting_FaceTintSort_SSE Is Nothing Then c.Setting_FaceTintSort_SSE = FaceTintSortSettings.DefaultsForSse()
            Return c.Setting_FaceTintSort_SSE
        End If
        If c.Setting_FaceTintSort Is Nothing Then c.Setting_FaceTintSort = New FaceTintSortSettings()
        Return c.Setting_FaceTintSort
    End Function
    Private Function NewSortDefaults() As FaceTintSortSettings
        Return If(_sortIsSse, FaceTintSortSettings.DefaultsForSse(), New FaceTintSortSettings())
    End Function

    Private Sub InitSortUi(c As Config_App)
        _sortIsSse = (c.Game = Config_App.Game_Enum.Skyrim)
        _tintKeyValues = PopulateKeyCombo(ComboTintKey, SortTintEnumType())
        _swapKeyValues = PopulateKeyCombo(ComboSwapKey, SortSwapEnumType())

        Dim s = ActiveSortSettings(c)
        _tintRules = CloneRules(If(s IsNot Nothing, s.TintRules, Nothing))
        _swapRules = CloneRules(If(s IsNot Nothing, s.SwapRules, Nothing))
        ' Se muestra TAL CUAL lo guardado: si guardaste una lista vacía, queda vacía (empty es intencional).
        ' El default sólo aparece en un config nuevo (lo pone el constructor de FaceTintSortSettings).
        Dim placement = If(s IsNot Nothing, s.SkinTonePlacement, 0)
        If placement < 0 OrElse placement >= ComboSkinPlacement.Items.Count Then placement = 0
        ComboSkinPlacement.SelectedIndex = placement

        RefreshRuleList(ListTintRules, _tintRules, SortTintEnumType())
        RefreshRuleList(ListSwapRules, _swapRules, SortSwapEnumType())
    End Sub

    Private Function CloneRules(src As List(Of FaceTintSortRule)) As List(Of FaceTintSortRule)
        Dim r As New List(Of FaceTintSortRule)
        If src IsNot Nothing Then
            For Each x In src
                r.Add(New FaceTintSortRule With {.Key = x.Key, .Descending = x.Descending})
            Next
        End If
        Return r
    End Function

    ''' <summary>Puebla el combo con los nombres del enum y devuelve los VALORES reales (item i del combo
    ''' -> valor(i)). Soporta huecos en el enum (p.ej. PhysIndex eliminado): el .Key de la regla sale de
    ''' este mapeo, NO del SelectedIndex.</summary>
    Private Function PopulateKeyCombo(cb As ComboBox, enumType As Type) As List(Of Integer)
        Dim vals As New List(Of Integer)
        cb.Items.Clear()
        For Each v In [Enum].GetValues(enumType)
            cb.Items.Add([Enum].GetName(enumType, v))
            vals.Add(CInt(v))
        Next
        If cb.Items.Count > 0 Then cb.SelectedIndex = 0
        Return vals
    End Function

    ''' <summary>Repuebla un ListBox de reglas: "ClaveNombre  -  asc/desc". Preserva la selección.</summary>
    Private Sub RefreshRuleList(lb As ListBox, rules As List(Of FaceTintSortRule), enumType As Type)
        Dim sel = lb.SelectedIndex
        lb.BeginUpdate()
        lb.Items.Clear()
        For Each r In rules
            Dim nm As String = "?"
            If [Enum].IsDefined(enumType, r.Key) Then nm = [Enum].GetName(enumType, r.Key)
            lb.Items.Add($"{nm}  -  {If(r.Descending, "desc", "asc")}")
        Next
        lb.EndUpdate()
        If sel >= 0 AndAlso sel < lb.Items.Count Then lb.SelectedIndex = sel
    End Sub

    Private Sub MoveRule(lb As ListBox, rules As List(Of FaceTintSortRule), enumType As Type, delta As Integer)
        Dim i = lb.SelectedIndex
        Dim j = i + delta
        If i < 0 OrElse i >= rules.Count OrElse j < 0 OrElse j >= rules.Count Then Return
        Dim tmp = rules(i) : rules(i) = rules(j) : rules(j) = tmp
        RefreshRuleList(lb, rules, enumType)
        lb.SelectedIndex = j
    End Sub

    Private Sub BtnTintAdd_Click(sender As Object, e As EventArgs) Handles BtnTintAdd.Click
        If ComboTintKey.SelectedIndex < 0 Then Return
        _tintRules.Add(New FaceTintSortRule With {.Key = _tintKeyValues(ComboTintKey.SelectedIndex), .Descending = ChkTintDesc.Checked})
        RefreshRuleList(ListTintRules, _tintRules, SortTintEnumType())
        ListTintRules.SelectedIndex = _tintRules.Count - 1
    End Sub
    Private Sub BtnTintRemove_Click(sender As Object, e As EventArgs) Handles BtnTintRemove.Click
        Dim i = ListTintRules.SelectedIndex
        If i < 0 OrElse i >= _tintRules.Count Then Return
        _tintRules.RemoveAt(i)
        RefreshRuleList(ListTintRules, _tintRules, SortTintEnumType())
    End Sub
    Private Sub BtnTintUp_Click(sender As Object, e As EventArgs) Handles BtnTintUp.Click
        MoveRule(ListTintRules, _tintRules, SortTintEnumType(), -1)
    End Sub
    Private Sub BtnTintDown_Click(sender As Object, e As EventArgs) Handles BtnTintDown.Click
        MoveRule(ListTintRules, _tintRules, SortTintEnumType(), 1)
    End Sub

    Private Sub BtnSwapAdd_Click(sender As Object, e As EventArgs) Handles BtnSwapAdd.Click
        If ComboSwapKey.SelectedIndex < 0 Then Return
        _swapRules.Add(New FaceTintSortRule With {.Key = _swapKeyValues(ComboSwapKey.SelectedIndex), .Descending = ChkSwapDesc.Checked})
        RefreshRuleList(ListSwapRules, _swapRules, SortSwapEnumType())
        ListSwapRules.SelectedIndex = _swapRules.Count - 1
    End Sub
    Private Sub BtnSwapRemove_Click(sender As Object, e As EventArgs) Handles BtnSwapRemove.Click
        Dim i = ListSwapRules.SelectedIndex
        If i < 0 OrElse i >= _swapRules.Count Then Return
        _swapRules.RemoveAt(i)
        RefreshRuleList(ListSwapRules, _swapRules, SortSwapEnumType())
    End Sub
    Private Sub BtnSwapUp_Click(sender As Object, e As EventArgs) Handles BtnSwapUp.Click
        MoveRule(ListSwapRules, _swapRules, SortSwapEnumType(), -1)
    End Sub
    Private Sub BtnSwapDown_Click(sender As Object, e As EventArgs) Handles BtnSwapDown.Click
        MoveRule(ListSwapRules, _swapRules, SortSwapEnumType(), 1)
    End Sub

    ''' <summary>Vuelve el orden (tints/swaps/placement) a los defaults del CÓDIGO (constructor de
    ''' FaceTintSortSettings). En memoria: el OK persiste, Cancel descarta. Independiente del Revert de
    ''' Conventions (no toca nada del otro tab).</summary>
    ''' <summary>"Revert to default" del tab Fixes. Mismo contrato que <see cref="BtnSortRevert_Click"/>:
    ''' toca SOLO el estado de la UI (los valores se escriben al config recién en el OK) y es GAME-AWARE.
    ''' <para><b>Los defaults NO se hardcodean acá</b>: salen de instancias frescas de las clases de config, o
    ''' sea de la MISMA declaración de la propiedad que define el default real. Si mañana cambia un default,
    ''' este botón lo sigue solo; una lista de literales duplicada se desincronizaría en silencio (y de hecho
    ''' el default de la ley de skin ya cambió una vez: False → True).</para>
    ''' <para><b>No cruza sets</b>, igual que el revert de Order: revierte únicamente las opciones del juego
    ''' ACTIVO. Las del otro juego se dejan intactas para que hagan round-trip sin tocarse — que es justo lo
    ''' que hace el Load/OK de esta pantalla con las opciones del juego inactivo.</para></summary>
    Private Sub BtnFixesRevert_Click(sender As Object, e As EventArgs) Handles BtnFixesRevert.Click
        Dim cfgDef As New Config_App()      ' defaults de Config_App tal como están declarados
        Dim npcDef As New NPC_Config()      ' defaults de NPC_Config tal como están declarados
        Dim isFo4 = (Config_App.Current.Game = Config_App.Game_Enum.Fallout4)

        If isFo4 Then
            CheckBoxApplyGhoulHeadRearFix.Checked = npcDef.ApplyGhoulHeadRearFix
            CheckBoxApplyEyebrowsFixedColor.Checked = cfgDef.Setting_ApplyEyebrowsFixedColor
            CheckBoxApplyMouthVanillaFix.Checked = cfgDef.Setting_ApplyMouthVanillaFix
            ' Default True (FO4): replicar la normalización de pesos del motor. Ver EngineSkinWeightNormalization.
            CheckBoxReplicateEngineSkinNorm.Checked = npcDef.ReplicateEngineSkinWeightNormalization
        Else
            CheckBoxBakeSseRaceMenuOverlays.Checked = cfgDef.Setting_BakeSseRaceMenuOverlays
            CheckBoxResolveHphHeadTri.Checked = cfgDef.Setting_SseResolveHighPolyHeadTri
        End If
    End Sub

    Private Sub BtnSortRevert_Click(sender As Object, e As EventArgs) Handles BtnSortRevert.Click
        ' GAME-AWARE: SSE → DefaultsForSse() (Race_Order/Ovl_Index = RaceMenu); FO4 → New() (defaults FO4). No cruza sets.
        Dim def = NewSortDefaults()
        _tintRules = CloneRules(def.TintRules)
        _swapRules = CloneRules(def.SwapRules)
        RefreshRuleList(ListTintRules, _tintRules, SortTintEnumType())
        RefreshRuleList(ListSwapRules, _swapRules, SortSwapEnumType())
        Dim p = def.SkinTonePlacement
        If p < 0 OrElse p >= ComboSkinPlacement.Items.Count Then p = 0
        ComboSkinPlacement.SelectedIndex = p
    End Sub

    ''' <summary>Carga los 3 buckets de una convención + el flag seed a los combos. Reusado por Load y Revert.</summary>
    ''' <summary>El set de convención del JUEGO ACTIVO: FO4 → Setting_FaceTintConvention; SSE →
    ''' Setting_FaceTintConvention_SSE (creado con DefaultsFor(Skyrim) si fuese Nothing). Dos configuraciones
    ''' separadas — el tab de convenciones edita la del juego activo sin tocar la otra. Espejo de
    ''' FaceTintConvention.ActiveSettings pero devuelve el objeto persistible (no la copia default).</summary>
    Private Function ActiveConventionSettings(c As Config_App) As FaceTintConvention.FaceTintConventionSettings
        If c.Game = Config_App.Game_Enum.Skyrim Then
            If c.Setting_FaceTintConvention_SSE Is Nothing Then
                c.Setting_FaceTintConvention_SSE = FaceTintConvention.FaceTintConventionSettings.DefaultsFor(Config_App.Game_Enum.Skyrim)
            End If
            Return c.Setting_FaceTintConvention_SSE
        End If
        If c.Setting_FaceTintConvention Is Nothing Then c.Setting_FaceTintConvention = New FaceTintConvention.FaceTintConventionSettings()
        Return c.Setting_FaceTintConvention
    End Function

    Private Sub LoadConvention(s As FaceTintConvention.FaceTintConventionSettings)
        LoadBucket(s.Diffuse, ComboDWork, ComboDComp, ComboDSrc, ComboDOut, ComboDMask, ComboDFw, ComboDSoft)
        LoadBucket(s.NormalSpecular, ComboNWork, ComboNComp, ComboNSrc, ComboNOut, ComboNMask, ComboNFw, ComboNSoft)
        LoadBucket(s.Swap, ComboSWork, ComboSComp, ComboSSrc, ComboSOut, ComboSMask, ComboSFw, ComboSSoft)
        LoadDWsByOp(s.DiffuseWorkingSpaceByBlend)
        CheckDSeedG22.Checked = s.SeedDiffuseG22
        ' Src de las TEXTURAS tint del diffuse (separado del Src del color sólido = ComboDSrc). Lo consume el
        ' resolver para isTextureSet (+ el base-seed). El Revert lo resetea (LoadConvention con un default fresco).
        ComboDTexSrc.SelectedIndex = CInt(s.DiffuseTextureSrcSpace)
    End Sub

    ''' <summary>Carga los 5 working-space POR BLEND OP del diffuse (parametrizable, engine-faithful por
    ''' default: SoftLight=G22, resto=Linear). Null-safe (config viejo sin la sección -&gt; defaults).</summary>
    Private Sub LoadDWsByOp(w As FaceTintConvention.FaceTintBlendWorkingSpaces)
        If w Is Nothing Then w = New FaceTintConvention.FaceTintBlendWorkingSpaces()
        ComboDWsReplace.SelectedIndex = CInt(w.Replace)
        ComboDWsMultiply.SelectedIndex = CInt(w.Multiply)
        ComboDWsOverlay.SelectedIndex = CInt(w.Overlay)
        ComboDWsSoftLight.SelectedIndex = CInt(w.SoftLight)
        ComboDWsHardLight.SelectedIndex = CInt(w.HardLight)
    End Sub

    ''' <summary>Escribe los 5 working-space por blend op del diffuse (índice del combo = valor del enum).</summary>
    Private Sub SaveDWsByOp(w As FaceTintConvention.FaceTintBlendWorkingSpaces)
        w.Replace = CType(ComboDWsReplace.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        w.Multiply = CType(ComboDWsMultiply.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        w.Overlay = CType(ComboDWsOverlay.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        w.SoftLight = CType(ComboDWsSoftLight.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        w.HardLight = CType(ComboDWsHardLight.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
    End Sub

    ''' <summary>Vuelca un bucket concreto a sus 7 combos (índice del combo = valor del enum, 0-based).</summary>
    Private Sub LoadBucket(b As FaceTintConvention.FaceTintBucketConvention,
                           cbWork As ComboBox, cbComp As ComboBox, cbSrc As ComboBox, cbOut As ComboBox,
                           cbMask As ComboBox, cbFw As ComboBox, cbSoft As ComboBox)
        cbWork.SelectedIndex = CInt(b.WorkingSpace)
        cbComp.SelectedIndex = CInt(b.CompositeSpace)
        cbSrc.SelectedIndex = CInt(b.SrcSpace)
        cbOut.SelectedIndex = CInt(b.OutputSpace)
        cbMask.SelectedIndex = CInt(b.MaskConv)
        cbFw.SelectedIndex = CInt(b.Framework)
        cbSoft.SelectedIndex = CInt(b.SoftLight)
    End Sub

    ''' <summary>Escribe los 7 combos de vuelta a un bucket concreto (valor del enum = índice del combo).</summary>
    Private Sub SaveBucket(b As FaceTintConvention.FaceTintBucketConvention,
                           cbWork As ComboBox, cbComp As ComboBox, cbSrc As ComboBox, cbOut As ComboBox,
                           cbMask As ComboBox, cbFw As ComboBox, cbSoft As ComboBox)
        b.WorkingSpace = CType(cbWork.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        b.CompositeSpace = CType(cbComp.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        b.SrcSpace = CType(cbSrc.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        b.OutputSpace = CType(cbOut.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)
        b.MaskConv = CType(cbMask.SelectedIndex, FaceTintConvention.FaceTintMaskConv)
        b.Framework = CType(cbFw.SelectedIndex, FaceTintConvention.FaceTintFramework)
        b.SoftLight = CType(cbSoft.SelectedIndex, FaceTintConvention.FaceTintSoftLight)
    End Sub

    ''' <summary>Revert to default: carga en los combos los defaults DEL JUEGO ACTIVO (DefaultsFor(game) — FO4
    ''' = ley derivada de fábrica; SSE = ley facegen-tint). ⛔ CRÍTICO: antes usaba New FaceTintConventionSettings()
    ''' (= SIEMPRE FO4), que en SSE cargaba la ley FO4 y al OK la persistía en el set SSE → render SSE ROTO. Ahora
    ''' es game-aware. Recién al dar OK se persisten; Cancel los descarta.</summary>
    Private Sub ButtonResetConv_Click(sender As Object, e As EventArgs) Handles ButtonResetConv.Click
        Dim game = If(Config_App.Current IsNot Nothing, Config_App.Current.Game, Config_App.Game_Enum.Fallout4)
        LoadConvention(FaceTintConvention.FaceTintConventionSettings.DefaultsFor(game))
    End Sub

    ''' <summary>Enum de resolución -> índice del combo (Inherit=0 ; 512..8192 = 1..5).</summary>
    Private Function ResToIndex(res As FaceTintConvention.FaceTintChannelResolution) As Integer
        If res = FaceTintConvention.FaceTintChannelResolution.Inherit Then Return 0
        Dim n = CInt(res)
        If n < 1 OrElse n > 5 Then Return 0
        Return n
    End Function

    ''' <summary>Índice del combo -> enum de resolución (0=Inherit ; 1..5 = 512..8192).</summary>
    Private Function IndexToRes(idx As Integer) As FaceTintConvention.FaceTintChannelResolution
        If idx <= 0 Then Return FaceTintConvention.FaceTintChannelResolution.Inherit
        Return CType(idx, FaceTintConvention.FaceTintChannelResolution)
    End Function

    ''' <summary>Habilita/deshabilita N/S (tamaño Y formato) según el modo. En "All" los deshabilita y los
    ''' hace seguir a D: el tamaño copia el índice del Diffuse; el formato deriva (Uncompressed si el Diffuse
    ''' es Uncompressed, sino BC5) — misma sincronía que el tamaño.</summary>
    Private Sub UpdateEnabledState()
        ' SSE SÍ bakea NORMAL (_msn plegado) → resolución+formato del Normal SON settables en per-layer (el bake resamplea
        ' con el filtro FO4). SSE NO bakea Specular → siempre deshabilitado. En FO4 N y S dependen del modo Per-layer.
        Dim isSse = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim perLayer = RadioPerLayer.Checked
        ' Enable IGUAL a FO4 en AMBOS juegos: en All-mode los combos N (resolución y formato) van DESHABILITADOS y
        ' DERIVAN del Diffuse; en per-layer se habilitan. Specular = FO4-only (SSE no bakea).
        ComboNormal.Enabled = perLayer
        ComboSpecular.Enabled = perLayer AndAlso Not isSse
        ComboFormatN.Enabled = perLayer
        ComboFormatS.Enabled = perLayer AndAlso Not isSse
        If Not perLayer Then
            ComboNormal.SelectedIndex = ComboDiffuse.SelectedIndex
            ComboSpecular.SelectedIndex = ComboDiffuse.SelectedIndex
            SetComboIndex(ComboFormatN, NormalFormatIndexFromDiffuse())     ' All: N deriva del D (game-aware: FO4→BC5, SSE→sigue el formato del D)
            SetComboIndex(ComboFormatS, SpecularFormatIndexFromDiffuse())   ' All: S deriva del D con la ley FO4 SIEMPRE (specular = FO4-only)
        End If
    End Sub

    ''' <summary>Fija el índice de un combo clampeando al rango de sus ítems (los combos de formato tienen distinto
    ''' número de ítems por juego/canal; un índice fuera de rango tira ArgumentOutOfRangeException).</summary>
    Private Shared Sub SetComboIndex(cb As ComboBox, idx As Integer)
        If cb.Items.Count = 0 Then Return
        cb.SelectedIndex = Math.Min(Math.Max(idx, 0), cb.Items.Count - 1)
    End Sub

    ''' <summary>Modo All: índice del combo N (formato) que el bake usará, GAME-AWARE. Combo N (enum
    ''' FaceTintNormalSpecularCompression): BC5=0/Uncompressed=1/BC7=2/BC3=3.
    ''' FO4: el _n es tangent-space 2-ch ⇒ deriva del Diffuse (BC5(0), o Uncompressed(1) si el D lo es).
    ''' SSE: el _msn es MODEL-SPACE 3-ch ⇒ SIEMPRE Uncompressed(1), NO deriva del Diffuse (cualquier BCn destruye la
    ''' dirección de la normal: MEDIDO BC3 → RMS 5.07/255, max 148/255 vs vanilla; Uncompressed → 0.000 = exacto).
    ''' Espeja exactamente FaceGenBuilder.OutputSettings (NormalCompressionAllModeSse) ⇒ la UI no miente sobre lo que
    ''' el bake escribe.</summary>
    Private Function NormalFormatIndexFromDiffuse() As Integer
        Dim isSse = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        If isSse Then Return 1   ' Uncompressed (SIEMPRE en SSE; el _msn model-space no tolera BCn)
        Return SpecularFormatIndexFromDiffuse()   ' FO4: misma ley que el specular (Uncompressed→1, sino BC5→0)
    End Function

    ''' <summary>Modo All: índice del combo S (formato) derivado del Diffuse. El specular es FO4-only (SSE no lo bakea),
    ''' así que usa la ley FO4 en AMBOS juegos — igual que FaceGenBuilder.OutputSettings, que en All-mode siempre
    ''' resuelve el specular con NsCompressionFromDiffuse: Uncompressed(1) si el D lo es, sino BC5(0). Aplicarle la ley
    ''' SSE del normal daba índice 3 (BC3), que ni existía en el combo (crash) ni es lo que el bake escribe.</summary>
    Private Function SpecularFormatIndexFromDiffuse() As Integer
        Return If(ComboFormat.SelectedIndex = 2, 1, 0)   ' Uncompressed→1, sino BC5→0
    End Function

    Private Sub RadioMode_CheckedChanged(sender As Object, e As EventArgs) Handles RadioAll.CheckedChanged, RadioPerLayer.CheckedChanged
        If _loading Then Return
        UpdateEnabledState()
    End Sub

    ''' <summary>Defaults del tab Size: All + Inherit (los 3) + Diffuse BC3 + Normal (FO4 BC5 / SSE Uncompressed) +
    ''' Specular BC5 + Generate TGA off (= los defaults del CÓDIGO/Config). En memoria; OK persiste, Cancel descarta.
    ''' Independiente de los Revert de Conventions/Order (no toca esos tabs).</summary>
    Private Sub ButtonResetSize_Click(sender As Object, e As EventArgs) Handles ButtonResetSize.Click
        Dim isSse = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        _loading = True
        RadioAll.Checked = True
        ComboDiffuse.SelectedIndex = 0      ' Inherit
        ComboNormal.SelectedIndex = 0
        ComboSpecular.SelectedIndex = 0
        ComboFormat.SelectedIndex = 0       ' BC3
        ' Normal: default del CÓDIGO = Uncompressed (índice 1) en SSE (el _msn model-space NO tolera BCn); BC5 (0) en FO4
        ' (tangent-space 2-ch). En FO4 modo All lo deriva UpdateEnabledState; en SSE el modo All también es Uncompressed.
        ComboFormatN.SelectedIndex = If(isSse, 1, 0)
        ComboFormatS.SelectedIndex = 0      ' BC5
        CheckGenerateTga.Checked = False
        _loading = False
        UpdateEnabledState()
    End Sub

    Private Sub ComboDiffuse_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboDiffuse.SelectedIndexChanged
        If _loading Then Return
        If RadioAll.Checked Then
            ComboNormal.SelectedIndex = ComboDiffuse.SelectedIndex
            ComboSpecular.SelectedIndex = ComboDiffuse.SelectedIndex
        End If
    End Sub

    ''' <summary>En "All", el formato N/S sigue al Diffuse. N: game-aware (FO4→BC5/Uncompressed; SSE→sigue el formato
    ''' del D). S: ley FO4 siempre (specular = FO4-only).</summary>
    Private Sub ComboFormat_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboFormat.SelectedIndexChanged
        If _loading Then Return
        If RadioAll.Checked Then
            SetComboIndex(ComboFormatN, NormalFormatIndexFromDiffuse())
            SetComboIndex(ComboFormatS, SpecularFormatIndexFromDiffuse())
        End If
    End Sub

    Private Sub ButtonOK_Click(sender As Object, e As EventArgs) Handles ButtonOK.Click
        Dim c = Config_App.Current
        c.Setting_FaceGenPerLayerResolution = RadioPerLayer.Checked
        c.Setting_FaceGenDiffuseResolution = IndexToRes(ComboDiffuse.SelectedIndex)
        If RadioPerLayer.Checked Then
            c.Setting_FaceGenNormalResolution = IndexToRes(ComboNormal.SelectedIndex)
            c.Setting_FaceGenSpecularResolution = IndexToRes(ComboSpecular.SelectedIndex)
        Else
            ' All: N/S heredan de Diffuse (se persiste el mismo valor).
            c.Setting_FaceGenNormalResolution = c.Setting_FaceGenDiffuseResolution
            c.Setting_FaceGenSpecularResolution = c.Setting_FaceGenDiffuseResolution
        End If
        Dim isSseSave = (c.Game = Config_App.Game_Enum.Skyrim)
        ' Compresiones PER-GAME. Diffuse → set del juego activo.
        Dim diffComp = CType(Math.Max(0, ComboFormat.SelectedIndex), FaceTintConvention.FaceTintDiffuseCompression)
        If isSseSave Then c.Setting_FaceGenDiffuseCompression_SSE = diffComp Else c.Setting_FaceGenDiffuseCompression = diffComp
        ' Normal: SOLO se persiste en PER-LAYER. En All-mode deriva del Diffuse dentro de OutputSettings (FO4→BC5,
        ' SSE→sigue el formato del D) ⇒ NO clobbereamos el valor per-layer guardado (así round-trip-ea al volver a per-layer).
        If RadioPerLayer.Checked Then
            Dim normComp = CType(Math.Max(0, ComboFormatN.SelectedIndex), FaceTintConvention.FaceTintNormalSpecularCompression)
            If isSseSave Then c.Setting_FaceGenNormalCompression_SSE = normComp Else c.Setting_FaceGenNormalCompression = normComp
            If Not isSseSave Then c.Setting_FaceGenSpecularCompression = CType(Math.Max(0, ComboFormatS.SelectedIndex), FaceTintConvention.FaceTintNormalSpecularCompression)   ' specular FO4-only
        End If
        c.Setting_FaceGenGenerateTga = CheckGenerateTga.Checked

        ' --- Tab "Fixes": toggle NPC-only → NPC_Config (no Config_App). Se flushea en el cierre de la app
        ' (MainForm_FormClosing → NPC_Config.SaveConfig()), igual que RenderGore. ---
        NPC_Config.Current.ApplyGhoulHeadRearFix = CheckBoxApplyGhoulHeadRearFix.Checked
        ' Ley del MOTOR → NPC_Config + re-aplicar el gate por juego INMEDIATAMENTE, porque el render
        ' del NPC actual se rehace al volver del OK y tiene que usar ya el modo elegido (RENDER == BAKE).
        NPC_Config.Current.ReplicateEngineSkinWeightNormalization = CheckBoxReplicateEngineSkinNorm.Checked
        NPC_Config.ApplyEngineSkinWeightNormalizationGate(c.Game)
        ' Eyebrows fixed-color gate → Config_App (lo lee la librería). Se persiste en el SaveConfig de abajo.
        c.Setting_ApplyEyebrowsFixedColor = CheckBoxApplyEyebrowsFixedColor.Checked
        ' Mouth vanilla fix gate → Config_App. Al volver el OK, MainForm re-renderiza el NPC actual; como la
        ' cache de TriHead está keyed por este flag, el re-render re-lee vanilla o fixed según corresponda.
        c.Setting_ApplyMouthVanillaFix = CheckBoxApplyMouthVanillaFix.Checked
        ' SSE bake-RaceMenu-overlays gate → Config_App (lo lee FaceGenBuilder.WriteSseFaceDiffuseWithOverlays).
        c.Setting_BakeSseRaceMenuOverlays = CheckBoxBakeSseRaceMenuOverlays.Checked
        ' SSE High Poly Head .tri redirect gate → Config_App. Al dar OK, MainForm re-renderiza el NPC actual;
        ' NpcMorphResolver relee el tri (cache keyed por path, no por este flag) al reconstruir el resolver.
        c.Setting_SseResolveHighPolyHeadTri = CheckBoxResolveHphHeadTri.Checked

        ' --- Tab "FaceTint Conventions": persistir la convención concreta por bucket, EN EL SET DEL JUEGO
        ' ACTIVO (FO4 → Setting_FaceTintConvention; SSE → Setting_FaceTintConvention_SSE). Dos configuraciones
        ' separadas; editar en SSE no toca la ley byte-exacta de FO4 y viceversa. ---
        Dim s = ActiveConventionSettings(c)
        SaveBucket(s.Diffuse, ComboDWork, ComboDComp, ComboDSrc, ComboDOut, ComboDMask, ComboDFw, ComboDSoft)
        SaveBucket(s.NormalSpecular, ComboNWork, ComboNComp, ComboNSrc, ComboNOut, ComboNMask, ComboNFw, ComboNSoft)
        SaveBucket(s.Swap, ComboSWork, ComboSComp, ComboSSrc, ComboSOut, ComboSMask, ComboSFw, ComboSSoft)
        If s.DiffuseWorkingSpaceByBlend Is Nothing Then s.DiffuseWorkingSpaceByBlend = New FaceTintConvention.FaceTintBlendWorkingSpaces()
        SaveDWsByOp(s.DiffuseWorkingSpaceByBlend)
        s.SeedDiffuseG22 = CheckDSeedG22.Checked
        s.DiffuseTextureSrcSpace = CType(ComboDTexSrc.SelectedIndex, FaceTintConvention.FaceTintWorkingSpace)

        ' --- Tab "Tint Order": persistir reglas de orden + SkinTonePlacement en el set DEL JUEGO ACTIVO
        ' (SSE → Setting_FaceTintSort_SSE ; FO4 → Setting_FaceTintSort). ActiveSortSettings crea el set si falta. ---
        Dim sortS = ActiveSortSettings(c)
        sortS.TintRules = _tintRules
        sortS.SwapRules = _swapRules
        sortS.SkinTonePlacement = Math.Max(0, ComboSkinPlacement.SelectedIndex)

        Config_App.SaveConfig()
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
