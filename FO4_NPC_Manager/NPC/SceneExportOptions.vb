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
    ''' el motor reemplaza el material entero y el color no se lee nunca. Ver <see cref="ShapeMaterialTranscriber"/>.</summary>
    Public Property WriteSkinTone As Boolean = True

    ''' <summary>FO4 únicamente: agregar el locator <c>LoadingMenuZoomTarget</c> como hijo directo de la
    ''' raíz. Es el pivote sobre el que la cámara del menú de carga orbita y hace zoom; sin él el motor
    ''' pivota en el origen del modelo (los pies).
    ''' <para>MEDIDO sobre los 173 <c>Meshes\LoadScreenArt\*.nif</c> vanilla: 63 lo traen, siempre como
    ''' NiNode vacío (sin hijos, sin extra data), hijo directo de la raíz, flags 14, rotación identidad y
    ''' escala 1. En SSE NO existe — 0 de 139 archivos; allá el encuadre viaja en el LSCR (SNAM/RNAM/XNAM).</para>
    ''' <para>NO exige <see cref="Skinned"/> = False. El nodo es inerte y no depende del skin; lo único
    ''' atado a unskinned es el caso VANILLA (los 173 loadscreens se referencian desde un STAT, que no
    ''' tiene esqueleto que deforme la malla, y los 173 son estáticos). Con skin el nodo se escribe igual
    ''' — sólo que el default de posición sale del modelo POSADO mientras la geometría queda en bind, así
    ''' que ahí el valor es aproximado.</para></summary>
    Public Property AddLoadScreenNode As Boolean = False

    ''' <summary>Cómo se coloca el <c>LoadingMenuZoomTarget</c>. Lo llena el diálogo con los valores que el
    ''' usuario tiene a la vista, así lo que se escribe es exactamente lo que se mostró.
    ''' <para>Sin valor —un caller headless que sólo prende el flag— el exporter arma el default con
    ''' <c>SceneNifExporter.MeasureBakedBounds</c>, que es la MISMA función con la que el diálogo arma el
    ''' suyo. No es un camino alternativo: es el mismo cálculo, hecho más tarde.</para></summary>
    Public Property LoadScreenNodePlacement As LoadScreenNodePlacement = Nothing

    ''' <summary>SSE únicamente: True = paths del camino PLEGADO (FaceDiffuse/FaceNormal en slots 0/1,
    ''' slots 3 y 6 neutralizados), que es lo que el bake escribe cuando el NPC tiene overlays de cara
    ''' o máscaras skee. False = camino del motor (facetint en el slot 6, slots 0/3 intactos).
    ''' Siempre False en FO4, donde no hay pliegue.</summary>
    Public Property FoldFaceOverlays As Boolean = False
End Class

''' <summary>
''' Colocación del locator <c>LoadingMenuZoomTarget</c>: los únicos tres campos que el NIF guarda para un
''' NiNode vacío. Vanilla los pone a mano en los 63 loadscreens de FO4 que lo traen, siempre con rotación
''' identidad y escala 1 — pero son editables porque el default geométrico sólo acierta en bípedos
''' erguidos.
''' </summary>
Public Class LoadScreenNodePlacement
    ''' <summary>Traslación en las coordenadas del NIF destino (= world del preview, porque el export
    ''' unskinned deja los shapes con T/R/S identidad).</summary>
    Public Property Position As System.Numerics.Vector3

    ''' <summary>Rotación en GRADOS POR EJE (X, Y, Z), no en el orden de parámetros de
    ''' <c>Transform_Class.EulerXYZToMatrix33</c>, que toma (yaw=Z, pitch=Y, roll=X). La conversión hace el
    ''' cruce explícito; acá se guarda por eje porque es lo que el usuario ve etiquetado en el diálogo.
    ''' <para>Vanilla: identidad en los 63 casos. Sirve para orientar al NPC hacia la cámara sin volver a
    ''' exportar.</para></summary>
    Public Property RotationDegrees As System.Numerics.Vector3

    ''' <summary>Escala del nodo. Vanilla: 1 en los 63 casos.</summary>
    Public Property Scale As Single = 1.0F
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
