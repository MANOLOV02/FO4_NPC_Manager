Imports FO4_Base_Library

''' <summary>
''' Diálogo "CharGen Options": tamaño de textura por canal del bake FaceGen + formato del diffuse.
''' Persiste en Config_App (config.json). Lógica de tamaño:
'''   - All (uniform): los 3 canales usan el tamaño Diffuse; Normal/Specular deshabilitados y siguen a D.
'''   - Per layer: cada canal su propio tamaño; los 3 habilitados.
''' Tamaño por canal: Inherit (MIP0 nativo, sin downgrade) o 512/1024/2048/4096/8192. Formato diffuse:
''' BC3 (default) o BC7. N/S siempre BC5 (no editable). Toda la UI vive en el .Designer.vb.
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
        ComboFormat.SelectedIndex = If(c.Setting_FaceGenDiffuseCompression = FaceTintConvention.FaceTintDiffuseCompression.Bc7, 1, 0)
        If c.Setting_FaceGenPerLayerResolution Then
            RadioPerLayer.Checked = True
        Else
            RadioAll.Checked = True
        End If

        ' --- Tab "FaceTint Conventions": cargar la convención concreta por bucket (índice = valor del enum). ---
        LoadConvention(c.Setting_FaceTintConvention)
        ' Blend es read-only: el combo deshabilitado muestra el valor fijo (record / Replace) del Designer.
        ComboDBlend.SelectedIndex = 0
        ComboNBlend.SelectedIndex = 0
        ComboSBlend.SelectedIndex = 0

        ' --- Tab "Tint Order": orden de composición configurable. ---
        InitSortUi(c)

        _loading = False
        UpdateEnabledState()
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
    Private Sub InitSortUi(c As Config_App)
        _tintKeyValues = PopulateKeyCombo(ComboTintKey, GetType(FaceTintSortKey))
        _swapKeyValues = PopulateKeyCombo(ComboSwapKey, GetType(FaceTintSwapSortKey))

        Dim s = c.Setting_FaceTintSort
        _tintRules = CloneRules(If(s IsNot Nothing, s.TintRules, Nothing))
        _swapRules = CloneRules(If(s IsNot Nothing, s.SwapRules, Nothing))
        ' Se muestra TAL CUAL lo guardado: si guardaste una lista vacía, queda vacía (empty es intencional).
        ' El default sólo aparece en un config nuevo (lo pone el constructor de FaceTintSortSettings).
        Dim placement = If(s IsNot Nothing, s.SkinTonePlacement, 0)
        If placement < 0 OrElse placement >= ComboSkinPlacement.Items.Count Then placement = 0
        ComboSkinPlacement.SelectedIndex = placement

        RefreshRuleList(ListTintRules, _tintRules, GetType(FaceTintSortKey))
        RefreshRuleList(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey))
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
        RefreshRuleList(ListTintRules, _tintRules, GetType(FaceTintSortKey))
        ListTintRules.SelectedIndex = _tintRules.Count - 1
    End Sub
    Private Sub BtnTintRemove_Click(sender As Object, e As EventArgs) Handles BtnTintRemove.Click
        Dim i = ListTintRules.SelectedIndex
        If i < 0 OrElse i >= _tintRules.Count Then Return
        _tintRules.RemoveAt(i)
        RefreshRuleList(ListTintRules, _tintRules, GetType(FaceTintSortKey))
    End Sub
    Private Sub BtnTintUp_Click(sender As Object, e As EventArgs) Handles BtnTintUp.Click
        MoveRule(ListTintRules, _tintRules, GetType(FaceTintSortKey), -1)
    End Sub
    Private Sub BtnTintDown_Click(sender As Object, e As EventArgs) Handles BtnTintDown.Click
        MoveRule(ListTintRules, _tintRules, GetType(FaceTintSortKey), 1)
    End Sub

    Private Sub BtnSwapAdd_Click(sender As Object, e As EventArgs) Handles BtnSwapAdd.Click
        If ComboSwapKey.SelectedIndex < 0 Then Return
        _swapRules.Add(New FaceTintSortRule With {.Key = _swapKeyValues(ComboSwapKey.SelectedIndex), .Descending = ChkSwapDesc.Checked})
        RefreshRuleList(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey))
        ListSwapRules.SelectedIndex = _swapRules.Count - 1
    End Sub
    Private Sub BtnSwapRemove_Click(sender As Object, e As EventArgs) Handles BtnSwapRemove.Click
        Dim i = ListSwapRules.SelectedIndex
        If i < 0 OrElse i >= _swapRules.Count Then Return
        _swapRules.RemoveAt(i)
        RefreshRuleList(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey))
    End Sub
    Private Sub BtnSwapUp_Click(sender As Object, e As EventArgs) Handles BtnSwapUp.Click
        MoveRule(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey), -1)
    End Sub
    Private Sub BtnSwapDown_Click(sender As Object, e As EventArgs) Handles BtnSwapDown.Click
        MoveRule(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey), 1)
    End Sub

    ''' <summary>Vuelve el orden (tints/swaps/placement) a los defaults del CÓDIGO (constructor de
    ''' FaceTintSortSettings). En memoria: el OK persiste, Cancel descarta. Independiente del Revert de
    ''' Conventions (no toca nada del otro tab).</summary>
    Private Sub BtnSortRevert_Click(sender As Object, e As EventArgs) Handles BtnSortRevert.Click
        Dim def As New FaceTintSortSettings()
        _tintRules = CloneRules(def.TintRules)
        _swapRules = CloneRules(def.SwapRules)
        RefreshRuleList(ListTintRules, _tintRules, GetType(FaceTintSortKey))
        RefreshRuleList(ListSwapRules, _swapRules, GetType(FaceTintSwapSortKey))
        Dim p = def.SkinTonePlacement
        If p < 0 OrElse p >= ComboSkinPlacement.Items.Count Then p = 0
        ComboSkinPlacement.SelectedIndex = p
    End Sub

    ''' <summary>Carga los 3 buckets de una convención + el flag seed a los combos. Reusado por Load y Revert.</summary>
    Private Sub LoadConvention(s As FaceTintConvention.FaceTintConventionSettings)
        LoadBucket(s.Diffuse, ComboDWork, ComboDComp, ComboDSrc, ComboDOut, ComboDMask, ComboDFw, ComboDSoft)
        LoadBucket(s.NormalSpecular, ComboNWork, ComboNComp, ComboNSrc, ComboNOut, ComboNMask, ComboNFw, ComboNSoft)
        LoadBucket(s.Swap, ComboSWork, ComboSComp, ComboSSrc, ComboSOut, ComboSMask, ComboSFw, ComboSSoft)
        CheckDSeedG22.Checked = s.SeedDiffuseG22
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

    ''' <summary>Revert to default: carga en los combos los defaults del CODIGO (constructor de
    ''' FaceTintConventionSettings = la ley derivada de fábrica), NO el último guardado en config. Recién
    ''' al dar OK se persisten; Cancel los descarta. Independiente de lo que haya en config_app.</summary>
    Private Sub ButtonResetConv_Click(sender As Object, e As EventArgs) Handles ButtonResetConv.Click
        LoadConvention(New FaceTintConvention.FaceTintConventionSettings())
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

    ''' <summary>Habilita/deshabilita N/S según el modo. En "All" los deshabilita y los hace seguir a D.</summary>
    Private Sub UpdateEnabledState()
        Dim perLayer = RadioPerLayer.Checked
        ComboNormal.Enabled = perLayer
        ComboSpecular.Enabled = perLayer
        If Not perLayer Then
            ComboNormal.SelectedIndex = ComboDiffuse.SelectedIndex
            ComboSpecular.SelectedIndex = ComboDiffuse.SelectedIndex
        End If
    End Sub

    Private Sub RadioMode_CheckedChanged(sender As Object, e As EventArgs) Handles RadioAll.CheckedChanged, RadioPerLayer.CheckedChanged
        If _loading Then Return
        UpdateEnabledState()
    End Sub

    Private Sub ComboDiffuse_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboDiffuse.SelectedIndexChanged
        If _loading Then Return
        If RadioAll.Checked Then
            ComboNormal.SelectedIndex = ComboDiffuse.SelectedIndex
            ComboSpecular.SelectedIndex = ComboDiffuse.SelectedIndex
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
        c.Setting_FaceGenDiffuseCompression = If(ComboFormat.SelectedIndex = 1,
            FaceTintConvention.FaceTintDiffuseCompression.Bc7, FaceTintConvention.FaceTintDiffuseCompression.Bc3)

        ' --- Tab "FaceTint Conventions": persistir la convención concreta por bucket. ---
        Dim s = c.Setting_FaceTintConvention
        SaveBucket(s.Diffuse, ComboDWork, ComboDComp, ComboDSrc, ComboDOut, ComboDMask, ComboDFw, ComboDSoft)
        SaveBucket(s.NormalSpecular, ComboNWork, ComboNComp, ComboNSrc, ComboNOut, ComboNMask, ComboNFw, ComboNSoft)
        SaveBucket(s.Swap, ComboSWork, ComboSComp, ComboSSrc, ComboSOut, ComboSMask, ComboSFw, ComboSSoft)
        s.SeedDiffuseG22 = CheckDSeedG22.Checked

        ' --- Tab "Tint Order": persistir reglas de orden + SkinTonePlacement. ---
        Dim sortS = c.Setting_FaceTintSort
        If sortS Is Nothing Then
            sortS = New FaceTintSortSettings()
            c.Setting_FaceTintSort = sortS
        End If
        sortS.TintRules = _tintRules
        sortS.SwapRules = _swapRules
        sortS.SkinTonePlacement = Math.Max(0, ComboSkinPlacement.SelectedIndex)

        Config_App.SaveConfig()
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
