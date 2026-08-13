Imports FO4_Base_Library
Imports NiflySharp
Imports NiflySharp.Blocks

''' <summary>
''' Reapunta los slots de textura de la CARA de un shape exportado a la salida del bake de FaceGen.
''' No compone ni escribe ninguna textura: sólo reescribe paths dentro del <c>BSShaderTextureSet</c>.
'''
''' <para>⭐ LOS PATHS SON LOS DEL BAKE, no inventados. Cada uno está copiado del sitio que los
''' escribe, para que el NIF exportado apunte exactamente adonde el bake deja (o dejará) el archivo:</para>
''' <list type="bullet">
''' <item>FO4 — <c>FaceGenBuilder</c> slotPlan + canonicalNifPath: slots <b>0/1/7</b> (no 0/1/2) a
''' <c>Data\Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\&lt;id&gt;{_d,_msn,_s}.dds</c>,
''' CON el prefijo <c>Data\</c>. En FO4 el tint va horneado DENTRO del <c>_d</c>: no hay slot de
''' facetint ni pliegue que elegir.</item>
''' <item>SSE camino del motor — <c>FaceGenBuilder</c> tintDir: slot <b>6</b> a
''' <c>data\Textures\Actors\Character\FaceGenData\FaceTint\&lt;plugin&gt;\&lt;id&gt;.dds</c>.</item>
''' <item>SSE camino PLEGADO — <c>WriteSseFaceDiffuseWithOverlays</c>: slot <b>0</b> a
''' <c>FaceDiffuse\…</c> y slot <b>1</b> a <c>FaceNormal\…</c>, ambos SIN prefijo.</item>
''' </list>
'''
''' <para>⛔ EL PREFIJO NO ES COSMÉTICO. En SSE el slot 6 es el ÚNICO que lleva <c>data\Textures\</c>
''' porque lo carga otro loader del motor; los demás van relativos a <c>Data\Textures\</c>. Un path
''' mal prefijado deja el slot en NULL y la cara sale MARRÓN (ley medida, 40-bake-leyes-sse.md).</para>
'''
''' <para>⛔ EL SLOT 6 SE ESCRIBE EN LOS DOS CAMINOS DE SSE. El bake pliega el diffuse pero deja
''' "slots 3/6 = REALES, cadena pre-compensada": el motor puede reinstalar el slot 3 desde el TXST al
''' attachear la cabeza, y del slot 6 deriva algo más que el albedo (subsurface), que no se puede
''' plegar en una textura de diffuse. Por eso el pliegue AGREGA slots, no los neutraliza.</para>
''' </summary>
Public NotInheritable Class FaceTextureRepointer

    Private Sub New()
    End Sub

    ' Slots FO4 del bake de cara. El specular es el 7, no el 2.
    Private Const Fo4SlotDiffuse As Integer = 0
    Private Const Fo4SlotNormal As Integer = 1
    Private Const Fo4SlotSpecular As Integer = 7
    ' Slots SSE.
    Private Const SseSlotDiffuse As Integer = 0
    Private Const SseSlotNormal As Integer = 1
    Private Const SseSlotFaceTint As Integer = 6

    ''' <summary>Qué se reescribió en un shape, para el resumen de la UI.</summary>
    Public Structure RepointOutcome
        ''' <summary>True si el shape era de cara y se le tocó al menos un slot.</summary>
        Public Repointed As Boolean
    End Structure

    ''' <summary>
    ''' Reapunta la cara de <paramref name="shape"/> si corresponde. Devuelve qué pasó.
    ''' <para>⭐ EL GATE ES EL SHADER TYPE, NO EL HEAD PART. El CK sólo redirige los slots cuando el
    ''' material del shape es <c>FaceTint</c> (RE CK 0x140ed9020 / 0x141d0ea00), y esto NO equivale a
    ''' <c>HDPT.PartType=Face</c>: hay 8 NPCs vanilla medidos (MaleHeadManekin y compañía) con
    ''' PartType=Face cuyo shape está autorado con shader type Default, y ahí el CK deja los slots
    ''' como están. Gatear por PartType nos desviaría del CK justo en esos.</para></summary>
    Public Shared Function Repoint(nif As Nifcontent_Class_Manolo, shape As INiShape,
                                   plan As FaceTexturePlan, foldOverlays As Boolean,
                                   Optional resolvedMaterial As FO4UnifiedMaterial_Class = Nothing) As RepointOutcome
        Dim outcome As New RepointOutcome With {.Repointed = False}
        If nif Is Nothing OrElse shape Is Nothing OrElse plan Is Nothing OrElse Not plan.IsUsable Then Return outcome

        Dim bsls = TryCast(nif.GetShader(shape), BSLightingShaderProperty)
        If bsls Is Nothing Then Return outcome
        If bsls.ShaderType_SK_FO4 <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Return outcome

        Dim hex = plan.FaceGenLocalFormID.ToString("X8")
        Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        ' ⭐ FO4: CORTAR EL LINK AL BGSM, o el repunte de abajo NO SE LEE NUNCA. Verificado en Fallout4.exe:
        ' con `prop+0x10` no vacío el motor carga el material y ApplyMaterialToGeometry (0x142169BB0)
        ' reemplaza el TEXTURE SET ENTERO (prop+0x1d0 ← mat+0x78, 0x142163B70); con el nombre vacío los 3
        ' call-sites de carga propia bailan en la guarda de largo (0x14167C300) y el shader inline manda.
        ' Se llama al MISMO método que usa el bake — no una copia. Copiarlo se probó y se desincronizó al
        ' toque (faltaba el centinela de Emissive y apagaba el Emissive en las 9 cabezas de FO4).
        '
        ' ⛔ SSE NO: allá el motor no carga materiales por nombre. MEDIDO sobre el 100% del corpus —
        '    0 de 4.025 shapes de head part traen nombre de material, así que no hay nada que cortar y
        '    transcribir sólo movería campos del shader a cambio de nada.
        ' ⛔ SIN material resuelto NO se corta el nombre: dejaría al shape con el shader inline del NIF
        '    fuente, que en las cabezas vanilla de FO4 es relleno (la autoridad es el BGSM). Sería cambiar
        '    un bug por otro. Se sigue por el camino de hoy: repunte inerte, pero sin regresión.
        If Not isSse Then
            If resolvedMaterial Is Nothing Then
                Dim shapeNameLog = If(shape.Name?.String, "")
                Logger.LogLazy(Function() $"[REPOINT] '{shapeNameLog}': sin material resuelto -> NO se corta el link al BGSM; el repunte queda inerte (igual que antes del fix).")
            Else
                ' CLON: `resolvedMaterial` es el material VIVO del render — el mismo objeto que dibuja el
                ' preview. La transcripción le escribe SkinTintAlpha, así que mutar el original le movería
                ' el shader al modelo en pantalla. El clone preserva también VetoAlphaPropertyCreation, que
                ' es lo que impide estrenarle un NiAlphaProperty a DiMA (paridad CK, ver 40-bake-leyes-fo4 §8).
                Dim mat = resolvedMaterial.Clone()
                FaceGenBuilder.TranscribeResolvedMaterialToShader(nif, shape, mat, mat.SkinTintAlpha)
            End If
        End If

        ' ⛔ DESPUÉS de la transcripción: Save_To_Shader escribe los 8 slots y puede CREAR el texture set.
        ' Resolverlo antes daría un bloque viejo, y repuntar antes lo pisaría con los paths vanilla.
        If bsls.TextureSetRef Is Nothing OrElse bsls.TextureSetRef.Index < 0 Then Return outcome
        Dim ts = TryCast(nif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
        If ts Is Nothing OrElse ts.Textures Is Nothing Then Return outcome

        If Not isSse Then
            ' ── FO4: los tres canales del bake, con prefijo Data\ ──
            Dim baseRel = $"Textures\Actors\Character\FaceCustomization\{plan.OriginPlugin}\{hex}"
            WriteSlot(ts, Fo4SlotDiffuse, "Data\" & baseRel & "_d.dds")
            WriteSlot(ts, Fo4SlotNormal, "Data\" & baseRel & "_msn.dds")
            WriteSlot(ts, Fo4SlotSpecular, "Data\" & baseRel & "_s.dds")
            outcome.Repointed = True
            Return outcome
        End If

        ' ── SSE: el facetint del slot 6 va SIEMPRE (existe en los dos caminos del bake) ──
        Dim tintRel = FaceGenPaths.TexturaDir(FaceGenPaths.CanalTint, plan.OriginPlugin) & hex & ".dds"
        WriteSlot(ts, SseSlotFaceTint, "data\" & tintRel)
        outcome.Repointed = True

        If Not foldOverlays Then Return outcome

        ' ── SSE plegado: el diffuse compuesto reemplaza al complexion, SIN prefijo ──
        Dim diffuseRel = FaceGenPaths.TexturaDir(FaceGenPaths.CanalDiffuse, plan.OriginPlugin) & hex & ".dds"
        WriteSlot(ts, SseSlotDiffuse, diffuseRel)

        ' El normal plegado es CONDICIONAL en el propio bake: sólo lo escribe si algún overlay aporta
        ' normal (gate HasFaceOverlayNormals, aparte del gate del diffuse). Si el bake no lo va a
        ' producir, el _msn del NIF fuente ES el correcto y pisarlo con un path muerto sería peor que
        ' no tocarlo.
        ' ⛔ El gate NO es la existencia del DDS en disco: eso mira sólo loose y un _msn empaquetado en
        ' un BA2 se leería como ausente. Es el MISMO predicado que el bake (HasFaceOverlayNormals),
        ' calculado por el resolver del render y traído en el plan.
        Dim normalRel = FaceGenPaths.TexturaDir(FaceGenPaths.CanalNormal, plan.OriginPlugin) & hex & ".dds"
        If plan.BakeEmitsFoldedNormal Then
            WriteSlot(ts, SseSlotNormal, normalRel)
        End If

        Return outcome
    End Function

    ''' <summary>Escribe <paramref name="embedded"/> en el slot, agrandando la lista si hace falta
    ''' (el patrón del bake).</summary>
    Private Shared Sub WriteSlot(ts As BSShaderTextureSet, slot As Integer, embedded As String)
        While ts.Textures.Count <= slot
            ts.Textures.Add(New NiString4 With {.Content = ""})
        End While
        If ts.Textures(slot) Is Nothing Then
            ts.Textures(slot) = New NiString4 With {.Content = embedded}
        Else
            ts.Textures(slot).Content = embedded
        End If
    End Sub


End Class
