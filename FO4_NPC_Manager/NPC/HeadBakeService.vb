Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics

''' <summary>
''' "FaceGeom en memoria": entrega, como <b>geometría BASE pre-skin</b> del shape PLANO, las mismas
''' posiciones que el bake de FaceGen escribiría a disco. Es lo que hace que el preview dibuje lo que
''' dibuja el juego.
'''
''' <para><b>El bug que cierra.</b> Ni <c>Fallout4.exe</c> ni el CK dibujan la malla <c>_faceBones</c>:
''' la usan como INSUMO de <c>ApplyCustomizationRemap</c> para calcular las posiciones de la malla
''' PLANA (el FaceGeom) y dibujan ésa. El preview, en cambio, redirigía a <c>_faceBones.nif</c> y
''' dibujaba ése ⇒ el body-weight caía sobre los 68 huesos de cara en vez de los ~10 del rig plano
''' (medido con <c>--headfidelity</c> sobre 1507 NPCs: rms <b>0,1107</b> / max <b>2,11</b>, contra un
''' control de 8,5e-14), y además se usaban los UV y el material del NIF equivocado (medido sobre 251
''' FaceGeom del BA2: donde el UV discrimina, el FaceGeom usa el del PLANO <b>227 a 0</b>).</para>
'''
''' <para><b>Dónde se engancha y por qué ahí.</b> Implementa
''' <see cref="FO4_Base_Library.IBaseGeometryProvider"/>, que el pipeline invoca EN SERIE al principio
''' de <c>PipelineStep_Morphs</c>. Ése es el único chokepoint por el que pasan los tres caminos de
''' render (full reload, pose+morphs, morph-only) ⇒ no hay camino que lo saltee. No se emite como canal
''' de morph a propósito: un canal pasa por el gate de bloques del CK, que existe para decodificar un
''' <c>.tri</c> comprimido — geometría calculada no es data de <c>.tri</c>.</para>
'''
''' <para><b>Absoluto, nunca incremental.</b> Cada refresh parte de una copia PRISTINA propia (tanto de
''' la base del shape plano como de los vértices del <c>_faceBones</c>, que el bake muta in place al
''' aplicar los morphs de chargen). Nunca lee el valor actual de <c>NifLocalVertices</c> ni
''' <c>geom.Vertices</c> ⇒ la realimentación que documenta <c>bug_morph_channel_feedback_loop</c> es
''' imposible por construcción, no por disciplina.</para>
'''
''' <para><b>Gate.</b> Sólo FO4 (<see cref="NPC_Config.IsHeadBakeActive"/>). En SSE no existe el mecanismo
''' <c>_faceBones</c>, así que no hay shapes gateadas y el servicio ni se construye.</para>
''' </summary>
Public Class HeadBakeService
    Implements FO4_Base_Library.IBaseGeometryProvider

    ''' <summary>Insumos de una shape gateada. Se registran al armar el shape set.</summary>
    Private NotInheritable Class Entry
        ''' <summary>NIF PLANO que se está dibujando (fresh per candidate — nadie más lo lee).</summary>
        Public FlatNif As Nifcontent_Class_Manolo
        Public FlatShape As INiShape
        ''' <summary>NIF <c>_faceBones</c>, insumo del cálculo. Se carga una vez y se reusa.</summary>
        Public FbnsNif As Nifcontent_Class_Manolo
        Public FbnsShape As INiShape
        ''' <summary>Copia pristina de los vértices del <c>_faceBones</c>: el bake los MUTA in place
        ''' (<c>ApplyChargenMorphsInPlace</c>), así que hay que restaurarlos antes de cada horneada.</summary>
        Public FbnsPristine As List(Of System.Numerics.Vector3)
        ''' <summary>Path CRUDO del chargen tri — SIEMPRE el real, sin gatear por el toggle. La decisión de
        ''' aplicarlo o no es VIVA (<see cref="_applyChargen"/>), porque el toggle "vertex morphs" se conmuta
        ''' por un camino que no re-registra las Entry; si el gate viviera acá, destildar no haría nada.</summary>
        Public ChargenTriPath As String = ""
        Public RaceMorphTriPath As String = ""
        ''' <summary>Firma de la última horneada aplicada a esta shape. "" = todavía ninguna.</summary>
        Public LastSignature As String = ""
        ''' <summary>Resultado de la última horneada, para reaplicar sin recalcular cuando el pipeline
        ''' vuelve a pasar por acá con la misma firma pero la base fue reseteada por una recarga.</summary>
        Public LastBaked As List(Of System.Numerics.Vector3)
    End Class

    Private ReadOnly _entries As New Dictionary(Of INiShape, Entry)()
    ''' <summary>Contexto del bake + firma vigentes. ⛔ MUTABLES y bajo <see cref="_gate"/> a propósito:
    ''' los toggles se conmutan por caminos que NO reconstruyen el servicio (los seis handlers granulares
    ''' llaman a <c>BuildCompositeMorphResolver</c> + <c>MarkDirty(Morphs)</c>, no a <c>BuildRenderPlan</c>).
    ''' Si la firma quedara congelada en el constructor, destildar "vertex morphs" no re-hornearía y —peor—
    ''' el filtro zap-only impediría que el face resolver restara los morphs ⇒ el checkbox quedaría MUERTO.
    ''' <see cref="UpdateInputs"/> es el punto por el que esos handlers refrescan ambos.</summary>
    Private _state As FaceGenBuildPipeline.BakeState
    Private _signature As String
    ''' <summary>Toggle "vertex morphs" VIVO. False ⇒ el bake NO aplica el chargen tri (base sin esos morphs).
    ''' Vive acá y no en la <see cref="Entry"/> porque el checkbox se conmuta por un camino que no re-registra.</summary>
    Private _applyChargen As Boolean = True
    ''' <summary>Protege <see cref="_state"/> / <see cref="_signature"/> / las <see cref="Entry"/>:
    ''' <c>UpdateInputs</c> lo llama el hilo de UI y <c>TryProvideBaseGeometry</c> el del pipeline.</summary>
    Private ReadOnly _gate As New Object()

    ''' <summary>Construye el servicio para UN NPC ya resuelto.</summary>
    ''' <param name="state">Contexto del bake (NPC con overlay, raza, pose FMRS). Lo arma el caller con
    ''' <see cref="FaceGenBuildPipeline.BuildBakeState"/>, honrando los toggles (ver
    ''' <see cref="BuildSignature"/> para qué entra en la firma).</param>
    ''' <param name="signature">Firma de los insumos: si no cambió, no se re-hornea.</param>
    Public Sub New(state As FaceGenBuildPipeline.BakeState, signature As String, applyChargen As Boolean)
        _state = state
        _signature = If(signature, "")
        _applyChargen = applyChargen
    End Sub

    ''' <summary>Refresca contexto + firma sin perder las shapes registradas (ni sus copias pristinas).
    ''' Lo llaman los handlers de toggle vía <c>MainForm.RefreshHeadBakeInputs</c>: si la firma cambió, el
    ''' próximo <see cref="TryProvideBaseGeometry"/> re-hornea. Devuelve True si algo cambió.</summary>
    Public Function UpdateInputs(state As FaceGenBuildPipeline.BakeState, signature As String, applyChargen As Boolean) As Boolean
        If state Is Nothing Then Return False
        Dim sig = If(signature, "")
        SyncLock _gate
            If sig = _signature Then Return False
            _state = state
            _signature = sig
            _applyChargen = applyChargen
        End SyncLock
        Return True
    End Function

    ''' <summary>Firma de TODO lo que el bake consume. Si algo de esto cambia hay que re-hornear; si no,
    ''' el refresh sale por el camino corto.
    ''' <para>⛔ Los TRES toggles entran a propósito, y cada uno por una razón medida:</para>
    ''' <list type="bullet">
    ''' <item><description><b>ApplyBoneMorphs</b> (= FMRS en FO4): antes la pose FMRS se aplicaba al
    ''' esqueleto y el checkbox la sacaba de ahí. Ahora la deformación vive en las posiciones horneadas
    ''' ⇒ sin esto el checkbox de FMRS quedaría muerto.</description></item>
    ''' <item><description><b>ApplyVertexMorphs</b>: el bake mete los morphs de chargen en la base
    ''' (fiel al CK). Sin esto, apagar el checkbox no los sacaría — <c>ApplyMorphPlan</c> resetea a
    ''' <c>NifLocalVertices</c>, que ya los contiene.</description></item>
    ''' <item><description><b>ApplyBodyWeight</b>: el bake incluye MWGT/MRSV en las shapes sin
    ''' <c>CustomizationRemapNewBonesData</c>. Sin esto la cabeza conservaría el peso mientras el cuerpo
    ''' lo pierde.</description></item>
    ''' </list>
    ''' <para>ApplySculpt NO entra: el bake nunca incluyó el sculpt de ARMA (decisión medida — el
    ''' FaceGeom se hornea una vez por NPC y no puede depender del outfit), y en el render sigue
    ''' entrando por escala de hueso, ahora sobre el rig plano, que es lo que hace el juego.</para>
    ''' <para>⛔ <b>LISTA COMPLETA, auditada contra el código (2026-07-22), no contra la memoria.</b> El bake
    ''' FO4 lee de <c>NpcData</c> EXACTAMENTE: <c>RaceFormID</c>, <c>IsFemale</c>, <c>MorphValues</c>,
    ''' <c>FaceMorphs</c>, <c>FacialMorphIntensity</c>, <c>BodyMorphRegionValues</c>,
    ''' <c>Weight{Thin,Muscular,Fat}</c> (grep sobre <c>BuildBakeState</c> + <c>FaceBonePoseBuilder</c> +
    ''' <c>NpcMorphResolver.BuildFaceMorphPlan</c> + <c>BuildBakeBodyWeightPose</c>). Los campos
    ''' <c>Nam/NamaRaw/SseCustomMorphs/SseSculpt*</c> son SSE-only y el head-bake es FO4-only ⇒ no entran.
    ''' Todos los demás están abajo. Si alguna vez el bake lee un campo nuevo de <c>NpcData</c>, HAY que
    ''' agregarlo acá o su slider queda muerto en el preview.</para></summary>
    Public Shared Function BuildSignature(npcData As NPC_Data, raceFormID As UInteger,
                                           toggles As RenderToggles) As String
        Dim sb As New System.Text.StringBuilder(256)
        sb.Append("r=").Append(raceFormID.ToString("X8"))
        If toggles IsNot Nothing Then
            sb.Append("|bm=").Append(If(toggles.ApplyBoneMorphs, "1", "0"))
            sb.Append("|vm=").Append(If(toggles.ApplyVertexMorphs, "1", "0"))
            sb.Append("|bw=").Append(If(toggles.ApplyBodyWeight, "1", "0"))
        End If
        If npcData IsNot Nothing Then
            sb.Append("|sex=").Append(If(npcData.IsFemale, "F", "M"))
            ' FMIN (FacialMorphIntensity): multiplicador lineal de la pose FMRS — lo lee el bake en
            ' FaceBonePoseBuilder. ⚠️ Va en la firma o el slider de FMIN quedaría muerto en el head-bake.
            sb.Append("|fmin=").Append(Fmt(npcData.FacialMorphIntensity))
            ' MWGT + MRSV (body-weight)
            sb.Append("|w=").Append(FmtN(npcData.WeightThin)).Append(",").
               Append(FmtN(npcData.WeightMuscular)).Append(",").Append(FmtN(npcData.WeightFat))
            If npcData.BodyMorphRegionValues IsNot Nothing Then
                sb.Append("|mrsv=")
                For Each v In npcData.BodyMorphRegionValues : sb.Append(Fmt(v)).Append(",") : Next
            End If
            ' Morphs de chargen (sliders) — orden estable por key.
            If npcData.MorphValues IsNot Nothing Then
                sb.Append("|mv=")
                For Each kv In npcData.MorphValues.OrderBy(Function(k) k.Key)
                    sb.Append(kv.Key.ToString("X8")).Append(":").Append(Fmt(kv.Value)).Append(",")
                Next
            End If
            ' FMRS (bone-region morphs). ⛔ Index + Values explícito, NO f.ToString(): NPC_FaceMorphData
            ' no sobreescribe ToString ⇒ devolvería el nombre del tipo y la firma quedaría CONSTANTE,
            ' con lo que mover un slider de FMRS no re-hornearía nunca (bug silencioso).
            If npcData.FaceMorphs IsNot Nothing Then
                sb.Append("|fm=").Append(npcData.FaceMorphs.Count).Append(":")
                For Each f In npcData.FaceMorphs
                    If f Is Nothing Then Continue For
                    sb.Append(f.Index.ToString("X8")).Append("=")
                    If f.Values IsNot Nothing Then
                        For Each v In f.Values : sb.Append(Fmt(v)).Append(" ") : Next
                    End If
                    sb.Append(",")
                Next
            End If
        End If
        Return sb.ToString()
    End Function

    Private Shared Function Fmt(v As Single) As String
        Return v.ToString("R", Globalization.CultureInfo.InvariantCulture)
    End Function
    Private Shared Function FmtN(v As Single?) As String
        Return If(v.HasValue, Fmt(v.Value), "-")
    End Function

    ''' <summary>Registra una shape gateada. La llama el collector cuando decidió NO redirigir.
    ''' <paramref name="fbnsNif"/>/<paramref name="fbnsShape"/> son el insumo del cálculo.</summary>
    Public Sub Register(flatNif As Nifcontent_Class_Manolo, flatShape As INiShape,
                        fbnsNif As Nifcontent_Class_Manolo, fbnsShape As INiShape,
                        chargenTriPath As String, raceMorphTriPath As String)
        If flatShape Is Nothing OrElse flatNif Is Nothing Then Return
        If fbnsNif Is Nothing OrElse fbnsShape Is Nothing Then Return
        SyncLock _gate
            If _entries.ContainsKey(flatShape) Then Return
        End SyncLock

        ' Copia pristina de los vértices del `_faceBones` ANTES de que el bake los toque.
        Dim pristine As List(Of System.Numerics.Vector3) = Nothing
        Try
            pristine = ShapeGeometryFactory.[For](fbnsShape, fbnsNif).GetVertexPositions().ToList()
        Catch ex As Exception
            Return
        End Try
        If pristine Is Nothing OrElse pristine.Count = 0 Then Return

        Dim ent As New Entry With {
            .FlatNif = flatNif,
            .FlatShape = flatShape,
            .FbnsNif = fbnsNif,
            .FbnsShape = fbnsShape,
            .FbnsPristine = pristine,
            .ChargenTriPath = If(chargenTriPath, ""),
            .RaceMorphTriPath = If(raceMorphTriPath, "")
        }
        SyncLock _gate
            _entries(flatShape) = ent
        End SyncLock
    End Sub

    ''' <summary>Cuántas shapes quedaron gateadas (diagnóstico / tests).</summary>
    Public ReadOnly Property RegisteredCount As Integer
        Get
            SyncLock _gate
                Return _entries.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>True si esta shape recibe la base horneada. Lo consulta el composite de morphs para
    ''' SUPRIMIR sus canales de POSICIÓN: el bake ya metió los morphs de chargen en la base (fiel al CK),
    ''' así que volver a emitirlos como canal los aplicaría DOS VECES. Los canales de zap sí pasan — son
    ''' máscaras por índice, agnósticas del espacio.</summary>
    Public Function IsGated(shape As IRenderableShape) As Boolean
        If shape Is Nothing OrElse shape.NifShape Is Nothing Then Return False
        SyncLock _gate
            Return _entries.ContainsKey(shape.NifShape)
        End SyncLock
    End Function

    ''' <summary>Entrega la base horneada. Ver el contrato en <see cref="IBaseGeometryProvider"/>:
    ''' escribe IN PLACE, es absoluto, y no lee <c>geom.Vertices</c> ni <c>geom.PerVertexSkinMatrix</c>.</summary>
    Public Function TryProvideBaseGeometry(shape As IRenderableShape, ByRef geom As SkinnedGeometry) As Boolean _
        Implements FO4_Base_Library.IBaseGeometryProvider.TryProvideBaseGeometry

        If shape Is Nothing OrElse shape.NifShape Is Nothing Then Return False
        Dim e As Entry = Nothing
        Dim sigNow As String
        SyncLock _gate
            If Not _entries.TryGetValue(shape.NifShape, e) Then Return False
            sigNow = _signature
        End SyncLock
        If geom.NifLocalVertices Is Nothing Then Return False

        ' Re-hornear sólo si la firma cambió. LastBaked se reaplica igual porque el pipeline puede
        ' haber recreado la SkinnedGeometry (full reload) y reseteado NifLocalVertices al valor del NIF.
        If e.LastSignature <> sigNow OrElse e.LastBaked Is Nothing Then
            Dim baked = Bake(e)
            If baked Is Nothing Then Return False
            e.LastBaked = baked
            e.LastSignature = sigNow
        End If

        If e.LastBaked.Count <> geom.NifLocalVertices.Length Then Return False
        ' IN PLACE: SkinnedGeometry es Structure; mutar los elementos del array propaga, reasignarlo no
        ' necesariamente. Además evita alocar por refresh.
        For i = 0 To e.LastBaked.Count - 1
            Dim v = e.LastBaked(i)
            geom.NifLocalVertices(i) = New Vector3d(v.X, v.Y, v.Z)
        Next
        Return True
    End Function

    ''' <summary>Restaura el rest pristino del `_faceBones` y corre el MISMO cálculo que el bake de disco
    ''' (<see cref="FaceGenBuildPipeline.ComputeBakedVertices"/>) — una sola implementación.</summary>
    Private Function Bake(e As Entry) As List(Of System.Numerics.Vector3)
        Dim st As FaceGenBuildPipeline.BakeState
        Dim applyChargen As Boolean
        SyncLock _gate
            st = _state
            applyChargen = _applyChargen
        End SyncLock
        If st Is Nothing Then Return Nothing
        Try
            ' SIEMPRE absoluto: el bake muta los vértices del `_faceBones` in place, así que se restaura
            ' el rest antes de cada horneada. Sin esto la segunda pasada morfearía sobre la primera.
            ShapeGeometryFactory.[For](e.FbnsShape, e.FbnsNif).SetVertexPositions(e.FbnsPristine)
        Catch ex As Exception
            Return Nothing
        End Try

        Try
            Return FaceGenBuildPipeline.ComputeBakedVertices(
                st, e.FlatNif, e.FlatShape, e.FbnsNif, e.FbnsShape,
                If(applyChargen, e.ChargenTriPath, ""), srcNif:=e.FlatNif, srcShape:=e.FlatShape,
                raceMorphTriPath:=e.RaceMorphTriPath)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

End Class
