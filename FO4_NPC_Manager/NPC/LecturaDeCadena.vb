Imports FO4_Base_Library

''' <summary>⛔⛔⛔ RONDA 20b (D2): DE DONDE SE LEEN LOS RECORDS DE UNA CADENA DE PLANTILLAS, Y CON QUE HOJA SE RESUELVE UNA
''' LISTA NIVELADA. Es la politica que comparten el guardado, el horneado y los tintes.
''' <para>Hasta la ronda 20a la escribian dos sedes por su cuenta: el guardado (`NpcOverrideSaver.FuenteDeTraitsParaGuardado`,
''' con el lector y la hoja del `SaveContext`) y el horneado (`NpcRecordOverlay.ResolveOverlaidNpcData`, que RE-PARSEABA del
''' plugin el record del NPC y el de cada eslabon). Con dos sedes el horneado no veia las ediciones de la sesion: el
''' render dibujaba con la raza editada de la plantilla y el `.nif`/`.dds` salian con la vieja (medido: medicion-d, d2).</para>
''' <para>⛔ La politica POR DEFECTO vive SOLO en <see cref="Resuelta"/>: sin lector, el parse del plugin; sin hoja, la de la
''' app sin pantalla (<c>NpcTemplateHelpers.HojaSinPantalla</c>). Quien construye una lectura dice lo que tiene y nada mas
''' (testigo: HerenciaAlDibujarGate G15-b).</para>
''' <para>⛔ Es parametro OBLIGATORIO de los consumidores, no `Optional ... = Nothing`: un Nothing ahi seria un centinela
''' que elige la politica en silencio, que es el error de la rev-48.</para></summary>
Public NotInheritable Class LecturaDeCadena

    ''' <summary>El orden de carga del que se parsea cuando no hay lector de sesion.</summary>
    Public PluginManager As PluginManager

    ''' <summary>FormID -> NPC. Nothing = el parse del plugin (<see cref="Resuelta"/>).</summary>
    Public Leer As Func(Of UInteger, NPC_Data)

    ''' <summary>Raiz de la cadena -> resolvedor de hoja de lista nivelada. Nothing (o que devuelva Nothing) = la politica
    ''' sin pantalla (<see cref="Resuelta"/>).</summary>
    Public HojaPara As Func(Of UInteger, Func(Of UInteger, UInteger))

    ''' <summary>⛔ LA UNICA SEDE DE LOS DEFAULTS: el lector y la hoja con los que se camina la cadena de
    ''' <paramref name="raiz"/>.</summary>
    Friend Function Resuelta(raiz As UInteger) As (Leer As Func(Of UInteger, NPC_Data), Hoja As Func(Of UInteger, UInteger))
        Dim pm = PluginManager
        Dim lector As Func(Of UInteger, NPC_Data) = Leer
        If lector Is Nothing Then lector = Function(f As UInteger) NpcRecordOverlay.GetParsedNpc(f, pm)
        Dim hoja As Func(Of UInteger, UInteger) = Nothing
        If HojaPara IsNot Nothing Then hoja = HojaPara(raiz)
        If hoja Is Nothing Then hoja = NpcTemplateHelpers.HojaSinPantalla(pm)
        Return (lector, hoja)
    End Function

End Class
