Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>LA SEDE ÚNICA de «un FormID a su record de head part», con los BORRADORES adentro.
'''
''' <para>Resuelve las tres firmas que un head part puede necesitar —<c>HDPT</c>, y los dos records a
''' los que apunta: <c>TXST</c> (por <c>TNAM</c>) y <c>FLST</c> (por <c>RNAM</c>)— mirando PRIMERO los
''' borradores en memoria y después el <see cref="PluginManager"/>.</para>
'''
''' <para>⛔ <b>Por qué es una clase y no un método de <c>NpcRenderContext</c>.</b> El BAKE no tiene
''' <c>NpcRenderContext</c> —lo dice <c>FaceGenBuilder.vb</c> en el comentario de
''' <c>ResolveOutfitHeadwearSlots</c>: «el bake no tiene NpcRenderContext»— así que una sede que viva
''' ahí adentro el bake no la puede alcanzar, y un head part BORRADOR se vería en el preview y sería
''' INVISIBLE para el FaceGeom. Eso es la divergencia RENDER≠BAKE, que en este árbol ya pasó una vez
''' por reparsear el record crudo en vez de honrar el overlay, y que se cerró con la regla que este
''' objeto hace cumplir: <i>same function + same inputs as the render = bake == render by
''' construction</i>. Acá el «same inputs» es literal: el render y el bake reciben LA MISMA INSTANCIA.</para>
'''
''' <para>⛔ <b>Por qué hay DOS puertas y ninguna acepta un delegado nulo.</b> El patrón anterior era
''' <c>Optional parseHdpt As Func(...) = Nothing</c> en cada función de <see cref="HeadPartResolver"/>,
''' o sea «<c>Nothing</c> = resolvé sin borradores». Eso es un CENTINELA: un llamador nuevo que se
''' olvida de pasarlo compila, corre, y pierde los borradores en silencio — que es exactamente el
''' defecto que esta sede vino a cerrar, entrando por la puerta de al lado. Acá el llamador tiene que
''' DECLARAR cuál de los dos mundos habita: <see cref="SinBorradores"/> (el CLI, los arneses de
''' <c>Tools\</c>, cualquier camino que no tenga editores abiertos) o <see cref="ConBorradores"/> (la
''' app), que TIRA si le pasan un delegado nulo.</para>
'''
''' <para>⛔ <b>Un borrador NO se cachea NUNCA acá.</b> Cachearlo dejaría el render y el bake
''' mostrando la versión de hace tres ediciones. Misma ley que <c>NpcRenderContext.ArmoDraftResolver</c>.</para>
'''
''' <para>⛔⛔ <b>Y esta sede NO es la que entrega la frescura: la entrega QUIEN MUTA, PUBLICA.</b> Lo
''' que los delegados de <see cref="ConBorradores"/> devuelven es una <b>FOTO</b> —el
''' <c>ParaRender</c> de <see cref="FotosDeBorrador(Of TVista)"/>, que es foto-sólo por su propio
''' contrato—, no el árbol vivo del borrador. O sea que «no se cachea nunca» describe lo que hace ESTE
''' objeto y sería una afirmación FALSA sobre el sistema si se leyera como «lo que devuelve está siempre
''' al día»: está al día porque el editor <b>publica</b> después de cada mutación, y por ninguna otra
''' razón. Decirlo importa en esta ola más que en las anteriores porque el editor de HDPT tiene
''' sub-edición <b>recursiva</b> —doble clic en un <c>HNAM</c> abre el mismo editor sobre otro record—,
''' así que los sitios que mutan son muchos y todos tienen la misma obligación. Lo levantó la revisión
''' adversarial [rev-27]: el doc afirmaba frescura y la sede no nombraba a quien la produce.</para>
'''
''' <para>⛔ <b>Y por eso memoizar el fracaso ACÁ SÍ es seguro, mientras el orden no se invierta.</b>
''' La caché guarda <c>Nothing</c> para un FormID que no resuelve, y eso es correcto SÓLO porque el
''' borrador se consulta ANTES de la caché: un FormID de borrador nunca llega a guardarse como
''' fracaso. El día que alguien invierta ese orden «por performance», reaparece el defecto que
''' <c>HeadPartResolver.IsHdptValidForRace</c> tenía con su <c>flstCache</c> local: cacheaba el
''' <c>Nothing</c> de una FLST que todavía no existía, el head part desaparecía del picker, y
''' <b>seguía desapareciendo aunque el usuario arreglara la FLST</b>, porque esa caché no se
''' invalidaba. Es la razón por la que la caché de FLST se mudó acá adentro y dejó de ser un parámetro.</para></summary>
Public NotInheritable Class ResolucionDeHeadParts

    ''' <summary>El orden de carga contra el que se resuelve. Fijo para la vida de este objeto.</summary>
    Public ReadOnly Plugins As PluginManager

    Private ReadOnly _borradorHdpt As Func(Of UInteger, Canon.IHdpt)
    Private ReadOnly _borradorTxst As Func(Of UInteger, Canon.ITxst)
    Private ReadOnly _borradorFlst As Func(Of UInteger, Canon.IFlst)

    ' Cachés de RECORDS REALES. Un borrador nunca entra acá (ver el ⛔ de la clase).
    Private ReadOnly _hdpt As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IHdpt)()
    Private ReadOnly _txst As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.ITxst)()
    Private ReadOnly _flst As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Canon.IFlst)()

    ''' <summary>True si este contexto puede devolver borradores. Lo lee el testigo, y lo lee el
    ''' mensaje de error de quien necesita saber por qué no ve el suyo.</summary>
    Public ReadOnly Property VeBorradores As Boolean
        Get
            Return _borradorHdpt IsNot Nothing
        End Get
    End Property

    Private Sub New(plugins As PluginManager,
                    borradorHdpt As Func(Of UInteger, Canon.IHdpt),
                    borradorTxst As Func(Of UInteger, Canon.ITxst),
                    borradorFlst As Func(Of UInteger, Canon.IFlst))
        If plugins Is Nothing Then
            Throw New ArgumentNullException(NameOf(plugins),
                "Sin PluginManager no hay contra qué resolver: un contexto así devolvería Nothing para " &
                "todo y el head part desaparecería sin un error.")
        End If
        Me.Plugins = plugins
        _borradorHdpt = borradorHdpt
        _borradorTxst = borradorTxst
        _borradorFlst = borradorFlst
    End Sub

    ''' <summary>Puerta para los caminos que NO tienen editores abiertos: el CLI headless y los arneses
    ''' de <c>Tools\</c>. Es EXPLÍCITA a propósito — «acá no hay borradores» es una declaración del
    ''' llamador, no un descuido que el tipo tolere.</summary>
    Public Shared Function SinBorradores(plugins As PluginManager) As ResolucionDeHeadParts
        Return New ResolucionDeHeadParts(plugins, Nothing, Nothing, Nothing)
    End Function

    ''' <summary>Puerta de la app. Los tres delegados son OBLIGATORIOS: cada uno que falte apaga en
    ''' silencio una clase entera de borrador, y el modo de falla es el peor que tiene este árbol —el
    ''' trabajo del usuario se pierde y nada avisa. Si un camino de verdad no tiene borradores, la
    ''' puerta que le toca es <see cref="SinBorradores"/>.</summary>
    Public Shared Function ConBorradores(plugins As PluginManager,
                                         borradorHdpt As Func(Of UInteger, Canon.IHdpt),
                                         borradorTxst As Func(Of UInteger, Canon.ITxst),
                                         borradorFlst As Func(Of UInteger, Canon.IFlst)) As ResolucionDeHeadParts
        If borradorHdpt Is Nothing OrElse borradorTxst Is Nothing OrElse borradorFlst Is Nothing Then
            Throw New ArgumentException(
                "ConBorradores necesita los TRES resolvedores. Uno nulo apaga una clase de borrador " &
                "entera sin un aviso: el head part (o su textura, o su lista de razas) se ve en el " &
                "editor y desaparece del render, del horneado o del guardado. Para un camino sin " &
                "borradores existe SinBorradores, que lo dice.")
        End If
        Return New ResolucionDeHeadParts(plugins, borradorHdpt, borradorTxst, borradorFlst)
    End Function

    '==============================================================================================
    ' Resolución
    '==============================================================================================

    ''' <summary>El HDPT de <paramref name="formID"/>: el borrador si hay uno, si no el record del orden
    ''' de carga. <c>Nothing</c> si no resuelve a un HDPT.</summary>
    Public Function Hdpt(formID As UInteger) As Canon.IHdpt
        If formID = 0UI Then Return Nothing
        If _borradorHdpt IsNot Nothing Then
            Dim b = _borradorHdpt(formID)
            If b IsNot Nothing Then Return b
        End If
        Return _hdpt.GetOrAdd(formID,
            Function(fid)
                Dim rec = Plugins.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return Nothing
                Return Canon.CanonRecords.Hdpt(rec, Plugins)
            End Function)
    End Function

    ''' <summary>El TXST de <paramref name="formID"/> (el <c>TNAM</c> de un head part, que en los ojos
    ''' ES la diffuse). Borrador primero. <c>Nothing</c> si no resuelve a un TXST.</summary>
    Public Function Txst(formID As UInteger) As Canon.ITxst
        If formID = 0UI Then Return Nothing
        If _borradorTxst IsNot Nothing Then
            Dim b = _borradorTxst(formID)
            If b IsNot Nothing Then Return b
        End If
        Return _txst.GetOrAdd(formID,
            Function(fid)
                Dim rec = Plugins.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then Return Nothing
                Return Canon.CanonRecords.Txst(rec, Plugins)
            End Function)
    End Function

    ''' <summary>La FLST de <paramref name="formID"/> (el <c>RNAM</c> de un head part: en qué razas es
    ''' válido). Borrador primero. <c>Nothing</c> si no resuelve a una FLST.</summary>
    Public Function Flst(formID As UInteger) As Canon.IFlst
        If formID = 0UI Then Return Nothing
        If _borradorFlst IsNot Nothing Then
            Dim b = _borradorFlst(formID)
            If b IsNot Nothing Then Return b
        End If
        Return _flst.GetOrAdd(formID,
            Function(fid)
                Dim rec = Plugins.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "FLST" Then Return Nothing
                Return Canon.CanonRecords.Flst(rec, Plugins)
            End Function)
    End Function

    '==============================================================================================
    ' Invalidación
    '==============================================================================================

    ''' <summary>Saca UN FormID de las tres cachés. Para después de una reversión en memoria, igual
    ''' que <c>NpcRenderContext.InvalidateRecord</c>: por clave, no con un <c>Clear()</c>.
    ''' <para>⛔ El motivo de que sea por clave es de ALCANCE, no de rendimiento: lo que cambió es UN
    ''' record, y vaciar las tres cachés obliga a re-resolver todo lo demás, que no cambió. Acá decía
    ''' además que un <c>Clear()</c> «a mitad de sesión compite con los renders de fondo»: eso NO ESTÁ
    ''' MEDIDO — no hay una medición de contención sobre estas tres cachés — y queda como lo que es,
    ''' una sospecha. Si alguna vez hace falta el <c>Clear()</c>, la decisión no se toma contra esa
    ''' frase.</para></summary>
    Public Sub Invalidar(formID As UInteger)
        If formID = 0UI Then Return
        Dim h As Canon.IHdpt = Nothing : _hdpt.TryRemove(formID, h)
        Dim t As Canon.ITxst = Nothing : _txst.TryRemove(formID, t)
        Dim f As Canon.IFlst = Nothing : _flst.TryRemove(formID, f)
    End Sub

    ''' <summary>Vacía las tres. Va en el cambio de orden de carga y **después de un guardado que
    ''' promovió borradores**: ahí los FormID provisionales murieron y los reales acaban de nacer, así
    ''' que cualquier resolución anterior es de otro mundo.</summary>
    Public Sub InvalidarTodo()
        _hdpt.Clear()
        _txst.Clear()
        _flst.Clear()
    End Sub

    ''' <summary>Cuántas entradas tiene cada caché. SÓLO LECTURA, para que un testigo pueda comprobar
    ''' que <see cref="InvalidarTodo"/> vacía de verdad — que por identidad de las vistas no se puede
    ''' observar, y por tiempo sería un umbral inventado.</summary>
    Public ReadOnly Property Cuentas As (Hdpt As Integer, Txst As Integer, Flst As Integer)
        Get
            Return (_hdpt.Count, _txst.Count, _flst.Count)
        End Get
    End Property

End Class
