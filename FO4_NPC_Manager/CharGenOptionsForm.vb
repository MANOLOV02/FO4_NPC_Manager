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
        _loading = False
        UpdateEnabledState()
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
        Config_App.SaveConfig()
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
