Imports FO4_Base_Library
Imports NiflySharp
Imports NiflySharp.Blocks

''' <summary>
''' La pasada de material del export: vuelca al shader inline el material que el RENDER ya resolvió,
''' para que el modelo exportado se vea como el preview y no como el NIF fuente.
''' <list type="bullet">
''' <item><b>FO4</b>: TODA shape. Transcribe el material completo y CORTA el nombre del .bgsm.</item>
''' <item><b>SSE</b>: sólo el color, y sólo en shapes de piel.</item>
''' </list>
''' <para>La CARA es de <see cref="FaceTextureRepointer"/>, que escribe este mismo shader.</para>
''' </summary>
Public NotInheritable Class ShapeMaterialTranscriber

    Private Sub New()
    End Sub

    Public Enum Outcome
        NotApplicable = 0
        Written = 1
        ''' <summary>SSE: piel tintada cuyo shader type no es SkinTint ⇒ el campo no se serializa. Se reporta.</summary>
        SkippedShaderType = 2
    End Enum

    ''' <param name="mat">El material que el render resolvió para este shape — el mismo objeto que dibujó
    ''' el preview.</param>
    ''' <param name="skinToneBaked"><c>MaterialData.SkinToneBaked</c>: el tono ya está en el diffuse.</param>
    Public Shared Function Apply(nif As Nifcontent_Class_Manolo, shape As INiShape,
                                 mat As FO4UnifiedMaterial_Class, skinToneBaked As Boolean) As Outcome
        If nif Is Nothing OrElse shape Is Nothing OrElse mat Is Nothing Then Return Outcome.NotApplicable

        ' Sin este gate la cara se transcribiría DOS veces: el repunte ya escribió su shader.
        If mat.Facegen Then Return Outcome.NotApplicable

        Dim shad = nif.GetShader(shape)
        If shad Is Nothing Then Return Outcome.NotApplicable

        Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        If isSse Then
            ' SSE no carga materiales por nombre ⇒ el shader inline ya es la ley y sólo falta el color.
            ' Transcribir el material entero movería campos a cambio de nada.
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            If bsls Is Nothing Then Return Outcome.NotApplicable
            If Not mat.SkinTint OrElse skinToneBaked Then Return Outcome.NotApplicable
            ' `Skin Tint Color` sólo existe en el layout con Shader Type == 5. Se mira el bloque DESTINO.
            If bsls.ShaderType_SK_FO4 <> NiflySharp.Enums.BSLightingShaderType.SkinTint Then Return Outcome.SkippedShaderType
            mat.WriteSkinTintToShader(bsls)
            Return Outcome.Written
        End If

        ' FO4: mientras el shape NOMBRE su .bgsm, ApplyMaterialToGeometry (0x142169BB0) reemplaza el
        ' material ENTERO y nada de lo que escribamos inline se lee. Por eso se transcribe TODA shape y no
        ' sólo la piel: con el gate viejo el PELO no entraba, salía nombrando su .BGSM y el motor le
        ' devolvía el color vanilla aunque el preview mostrara el del NPC.
        ' Costo aceptado: el shape deja de seguir al .bgsm en disco.
        ' No se filtra por Glowmap ni por ningún flag — el tipo horneado lo deriva la transcripción.
        If mat.SkinTint AndAlso skinToneBaked Then Return Outcome.NotApplicable   ' o se aplica dos veces

        ' CLON: `mat` es el material VIVO del render; la transcripción le escribe SkinTintAlpha y mutarlo
        ' movería el shader del modelo en pantalla.
        Dim matClone = mat.Clone()
        FaceGenBuilder.TranscribeResolvedMaterialToShader(nif, shape, matClone, matClone.SkinTintAlpha)
        Return Outcome.Written
    End Function

End Class
