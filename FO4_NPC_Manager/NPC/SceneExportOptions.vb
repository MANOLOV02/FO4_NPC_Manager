Imports FO4_Base_Library

''' <summary>Lo que el usuario eligió en <c>ExportModelOptions_Form</c> para el export a NIF.</summary>
Public Class SceneExportOptions
    ''' <summary>True = el NIF conserva esqueleto y pesos (vértices en bind pose, post-morph).
    ''' False = se hornea la pose actual en los vértices y se tira el skin.</summary>
    Public Property Skinned As Boolean = True

    ''' <summary>True = reescribir los paths de textura de la cara apuntando a la salida del bake de
    ''' FaceGen. NO bakea nada: sólo cambia los paths. False = la cara queda con los paths del NIF
    ''' fuente (vanilla, sin tint).</summary>
    Public Property RepointFaceTextures As Boolean = True

    ''' <summary>True = escribir el skin tone del NPC en el shader de los shapes de piel (SkinTint), para
    ''' que el modelo exportado salga con el tono del preview en vez del default del NIF fuente. En FO4
    ''' implica ADEMÁS transcribir el material resuelto al shader inline y cortar el link al .bgsm — sin eso
    ''' el motor reemplaza el material entero y el color no se lee nunca. Ver <see cref="SkinToneShaderWriter"/>.</summary>
    Public Property WriteSkinTone As Boolean = True

    ''' <summary>SSE únicamente: True = paths del camino PLEGADO (FaceDiffuse/FaceNormal en slots 0/1,
    ''' slots 3 y 6 neutralizados), que es lo que el bake escribe cuando el NPC tiene overlays de cara
    ''' o máscaras skee. False = camino del motor (facetint en el slot 6, slots 0/3 intactos).
    ''' Siempre False en FO4, donde no hay pliegue.</summary>
    Public Property FoldFaceOverlays As Boolean = False
End Class

''' <summary>
''' Los datos del NPC que hacen falta para armar los paths de la cara. Los arma el MainForm (que es
''' quien tiene el FormID y el PluginManager) y los consume el exporter.
''' </summary>
Public Class FaceTexturePlan
    ''' <summary>Plugin de origen del NPC — el subdirectorio que el motor usa bajo FaceGenData /
    ''' FaceCustomization.</summary>
    Public Property OriginPlugin As String

    ''' <summary>FormID LOCAL del NPC en formato FaceGen (el que nombra los DDS), ya resuelto con
    ''' <c>PluginManager.ToFaceGenLocalFormID</c>.</summary>
    Public Property FaceGenLocalFormID As UInteger

    ''' <summary>¿El bake va a emitir el _msn plegado para este NPC? Decide si el repunte pisa el
    ''' slot 1 en SSE plegado. Sale del MISMO predicado que el bake (HasFaceOverlayNormals) vía el
    ''' resolver del render; NO se pregunta por la existencia del archivo, que ignoraría los BA2.</summary>
    Public Property BakeEmitsFoldedNormal As Boolean

    Public ReadOnly Property IsUsable As Boolean
        Get
            Return Not String.IsNullOrEmpty(OriginPlugin)
        End Get
    End Property
End Class
