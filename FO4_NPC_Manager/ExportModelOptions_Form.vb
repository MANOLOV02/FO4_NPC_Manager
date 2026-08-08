Imports FO4_Base_Library

''' <summary>
''' Opciones del botón "NPC Model to NIF". Diálogo modal, chico y fijo: geometría skinned/unskinned
''' y qué hacer con las texturas de la cara.
''' <para>El sub-grupo de overlays existe SÓLO en SSE. En FO4 el bake hornea el tint DENTRO del
''' diffuse (los tres DDS de <c>FaceCustomization</c>), así que no hay pliegue que elegir.</para>
''' </summary>
Public Class ExportModelOptions_Form

    ''' <summary>Lo que el diálogo devuelve. Lo consume <see cref="SceneNifExporter.Export"/>.</summary>
    Public ReadOnly Property Options As New SceneExportOptions()

    ''' <summary>True si el NPC tiene overlays de cara o máscaras skee que el bake PLEGARÍA. Es el
    ''' default de la sub-opción de overlays; ver <see cref="Prepare"/>.</summary>
    Private _foldIsTheBakeDefault As Boolean

    ''' <summary>
    ''' Configura el diálogo para el NPC en pantalla.
    ''' <para><paramref name="npcFoldsOverlays"/> tiene que venir del RENDER
    ''' (<c>NpcFaceTintResolver.LastSseFoldWasMandatory</c>): dice si este NPC trae overlays de cara
    ''' o máscaras skee foldables.</para>
    ''' <para>⛔ El default combina ese dato CON el toggle "Bake RaceMenu overlays", porque la
    ''' condición del BAKE es la conjunción de los dos (FaceGenBuilder.WriteSseFaceDiffuseWithOverlays:
    ''' sale temprano si el toggle está apagado) y es el bake el que decide qué archivo va a existir.
    ''' El RENDER, en cambio, pliega mirando sólo los overlays — así que con el toggle apagado el
    ''' preview pliega y el bake no. Seguir al render acá apuntaría a un DDS que nadie escribe.</para>
    ''' </summary>
    Public Sub Prepare(isSse As Boolean, npcFoldsOverlays As Boolean)
        Dim bakeOverlaysOn As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                         Config_App.Current.Setting_BakeSseRaceMenuOverlays)
        _foldIsTheBakeDefault = isSse AndAlso npcFoldsOverlays AndAlso bakeOverlaysOn

        PanelOverlays.Visible = isSse
        RadioWithOverlays.Checked = _foldIsTheBakeDefault
        RadioWithoutOverlays.Checked = Not _foldIsTheBakeDefault
        UpdateFaceSubOptions()
    End Sub

    Private Sub ExportModelOptions_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        UpdateFaceSubOptions()
    End Sub

    ''' <summary>Las sub-opciones sólo tienen sentido si se van a reescribir los paths.</summary>
    Private Sub UpdateFaceSubOptions()
        PanelOverlays.Enabled = CheckUseFaceGenBake.Checked
    End Sub

    Private Sub CheckUseFaceGenBake_CheckedChanged(sender As Object, e As EventArgs) Handles CheckUseFaceGenBake.CheckedChanged
        UpdateFaceSubOptions()
    End Sub

    Private Sub ButtonExport_Click(sender As Object, e As EventArgs) Handles ButtonExport.Click
        Options.Skinned = RadioSkinned.Checked
        Options.RepointFaceTextures = CheckUseFaceGenBake.Checked
        ' Fuera de SSE el pliegue no existe: el panel está oculto y la opción queda en False para que
        ' el exporter tome siempre la rama de FO4.
        Options.FoldFaceOverlays = PanelOverlays.Visible AndAlso RadioWithOverlays.Checked
    End Sub

End Class
