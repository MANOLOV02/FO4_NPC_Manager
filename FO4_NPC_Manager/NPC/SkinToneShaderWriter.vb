Imports FO4_Base_Library
Imports NiflySharp
Imports NiflySharp.Blocks

''' <summary>
''' Escribe el SKIN TONE del NPC en el shader inline de los shapes de piel del NIF exportado, para que el
''' modelo salga con el tono que se ve en el preview en vez del default del NIF fuente.
'''
''' <para>⭐ EL COLOR NO SE RE-RESUELVE: sale del material que el RENDER ya resolvió para ESE shape
''' (SSE: <c>NpcFaceTintResolver</c> ~:1193 desde el QNAM; FO4: <c>NpcMaterialResolver</c> ~:1521 desde el
''' slot-12 / QNAM). Y el escritor tampoco es propio: es el mismo <c>WriteSkinTintToShader</c> que usa el
''' bake, así que la conversión a Color3 (byte/255, SIN gamma) es la única del proyecto.</para>
'''
''' <para>⛔ EL GATE ES EL DEL RENDER, no una regla nueva (Render.vb ~3765-3769): se tinta el shape cuyo
''' material resolvió <c>SkinTint</c> SALVO que el tono YA esté horneado en su diffuse
''' (<c>MaterialData.SkinToneBaked</c>). Escribirlo ahí lo aplicaría DOS VECES.</para>
'''
''' <para>⭐ SOBRE <c>SkinToneBaked</c> EN EL CUERPO: hoy NO PUEDE ser True en un shape de piel. El único
''' sitio que lo pone en True es <c>NpcFaceTintResolver</c> ~:514, dentro de un loop cortado en :311 por
''' <c>NifShaderType &lt;&gt; FaceTint ⇒ Continue For</c> — o sea, mallas de CARA solamente; el otro sitio
''' (~:285) sólo lo BAJA. Significa "el diffuse de esta malla salió compuesto en esta pasada", no "el NPC
''' tiene overlays". (El doc de Render.vb ~1959 menciona además un "Skyrim legacy BODY bake path": ese
''' camino ya no existe — no queda ninguna asignación así en el árbol.) Se conserva igual, porque es
''' paridad con el render y cuesta cero: si algún día el preview hornea un diffuse de cuerpo, el export
''' deja de duplicar el tono solo.</para>
'''
''' <para>⛔ EL CAMPO SÓLO VIAJA CON SHADER TYPE 5. `Skin Tint Color` está condicionado a
''' `Shader Type == 5 (SkinTint)` en el layout del NIF (nif.xml, BSLightingShaderProperty), y `Skin Tint
''' Alpha` existe sólo en FO4. Un shape de piel cuyo tipo efectivo NO sea 5 se SALTEA y se REPORTA: forzarle
''' el tipo cambiaría el sombreado, que es otra cosa que copiarle un color.</para>
'''
''' <para>⛔ FO4 ≠ SSE, y no por gusto:</para>
''' <list type="bullet">
''' <item><b>SSE</b>: el motor no carga materiales por nombre, así que el shader inline ES la ley y alcanza
''' con el Color3. Transcribir el material entero movería campos del shader a cambio de nada — el mismo
''' criterio que ya toma el repunte de la cara.</item>
''' <item><b>FO4</b>: mientras el shape nombre su <c>.bgsm</c>, <c>ApplyMaterialToGeometry</c> (0x142169BB0)
''' reemplaza el material ENTERO y el color inline no se lee nunca. Para que valga hay que hacer el mismo
''' gesto que la cara: transcribir el material resuelto + cortar el nombre
''' (<c>FaceGenBuilder.TranscribeResolvedMaterialToShader</c>, compartido con el bake). Eso vuelve al shape
''' autocontenido: DEJA de seguir al material en disco. Por eso la opción es del usuario.</item>
''' </list>
'''
''' <para>⚠️ Sobre un actor REAL in-game el motor recomputa el tono del record y pisa el campo del NIF
''' ("Overridden by game settings"): esto es para visores / Outfit Studio / el propio preview y para usos
''' no-actor (maniquí, prop estático), no para arreglar un NPC en el juego.</para>
''' </summary>
Public NotInheritable Class SkinToneShaderWriter

    Private Sub New()
    End Sub

    ''' <summary>Qué pasó con un shape. <see cref="SkippedShaderType"/> es el ÚNICO residuo que el usuario
    ''' tiene que saber: el shape era piel tintada y el tono no entró en el archivo.</summary>
    Public Enum Outcome
        ''' <summary>El shape no es piel tintada por el render (o el tono ya está horneado en su diffuse).</summary>
        NotApplicable = 0
        ''' <summary>Skin tone escrito en el shader del shape.</summary>
        Written = 1
        ''' <summary>Es piel tintada pero su shader type efectivo no es SkinTint ⇒ el campo no se serializa.</summary>
        SkippedShaderType = 2
    End Enum

    ''' <summary>Aplica el skin tone al shader de <paramref name="shape"/> dentro de
    ''' <paramref name="nif"/> (el NIF DESTINO del export).</summary>
    ''' <param name="mat">El material que el render resolvió para este shape — el mismo objeto que dibujó
    ''' el preview. Se clona antes de mutar nada (ver la rama de FO4).</param>
    ''' <param name="skinToneBaked">El "ya está" per-mesh del render (<c>MaterialData.SkinToneBaked</c>).</param>
    Public Shared Function Apply(nif As Nifcontent_Class_Manolo, shape As INiShape,
                                 mat As FO4UnifiedMaterial_Class, skinToneBaked As Boolean) As Outcome
        If nif Is Nothing OrElse shape Is Nothing OrElse mat Is Nothing Then Return Outcome.NotApplicable

        ' Gate del render, tal cual: SkinTint y el tono todavía NO horneado en el diffuse.
        If Not mat.SkinTint OrElse skinToneBaked Then Return Outcome.NotApplicable
        ' La CARA queda afuera aunque su material sea de piel (lo es): su tipo efectivo es FaceTint — Face
        ' gana sobre SkinTint en la cascada del factory (FO4UnifiedMaterial_Class.ResolveEffectiveType) — y
        ' con ese tipo el campo NO existe en el layout. Además tiene su propio camino (FaceTextureRepointer),
        ' que en FO4 ya transcribe este mismo shader. No es un residuo que reportar: es otro subsistema.
        If mat.Facegen Then Return Outcome.NotApplicable

        Dim bsls = TryCast(nif.GetShader(shape), BSLightingShaderProperty)
        If bsls Is Nothing Then Return Outcome.NotApplicable

        Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        If isSse Then
            ' En SSE el tipo del shader ES la ley del SkinTint (Create_From_Shader lee `.SkinTint` como
            ' `shad.IsTypeSkinTint`), así que el shape ya viene con el 5 salvo que la escena traiga un
            ' material que dice piel sobre un shader que no lo es. Se mira el bloque DESTINO, que es el que
            ' se va a serializar.
            If bsls.ShaderType_SK_FO4 <> NiflySharp.Enums.BSLightingShaderType.SkinTint Then Return Outcome.SkippedShaderType
            mat.WriteSkinTintToShader(bsls)
            Return Outcome.Written
        End If

        ' FO4: el tipo se DERIVA de los flags al transcribir (misma cascada que el bake del CK, ver
        ' FaceGenBuilder ~2221-2238: Glowmap > Facegen > SkinTint > Hair > Envmap > Default). Si algo le gana
        ' al SkinTint el tipo horneado no va a ser 5 y el color no viajaría — y entonces cortarle el link al
        ' .bgsm sería mutilar el shape a cambio de nada. Se saltea ANTES de tocarlo.
        If mat.Glowmap Then Return Outcome.SkippedShaderType

        ' CLON: `mat` es el material VIVO del render — el mismo objeto que dibuja el preview. La
        ' transcripción le escribe SkinTintAlpha, así que mutar el original le movería el shader al modelo en
        ' pantalla. Es el mismo recaudo que toma el repunte de la cara.
        Dim matClone = mat.Clone()
        FaceGenBuilder.TranscribeResolvedMaterialToShader(nif, shape, matClone, matClone.SkinTintAlpha)
        Return Outcome.Written
    End Function

End Class
