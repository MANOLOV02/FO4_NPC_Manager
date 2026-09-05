''' <summary>EL PROTOCOLO de un editor de borrador sobre el REGISTRO: se TOMA un FormID —y en ese mismo
''' gesto se captura todo lo que hace falta para poder deshacer y para decidir si quedó sucio— y después
''' se ABANDONA o se acepta.
'''
''' <para>⛔ <b>Es UNA clase y no tres copias adentro de tres formularios.</b> Las leyes que aplica
''' —cuándo dar de baja un registro, contra qué se compara la suciedad— vivían escritas a mano en
''' <c>ArmoEditor_Form</c>, <c>ArmaEditor_Form</c> y <c>MswpSubEditor_Form</c>. Un <c>Form</c> no se
''' instancia desde una consola, así que ninguna de las tres se podía MEDIR: es exactamente la forma en
''' que un gate pasa en vacío. Acá adentro no hay UI, y por eso el testigo las recorre.</para>
'''
''' <para>⛔ <b>Los cinco delegados TIRAN si vienen Nothing.</b> Cada uno apaga en silencio una mitad de
''' la ley, y el modo de falla es siempre el mismo: el editor sigue andando normal y el trabajo del
''' usuario se pierde sin un aviso. Es la misma regla que ya fija
''' <see cref="Borradores.ReidentificarComoClon"/>: un delegado nulo es error de LLAMADOR, no un dato
''' posible.</para>
'''
''' <para>⛔ <b>GENÉRICA, y por eso hay UNA ley y no tres.</b> Mismo motivo que
''' <see cref="FotosDeBorrador(Of TVista)"/>: los borradores NO comparten interfaz —no hay tipo común del
''' que colgar <c>.FormID</c> o <c>.Record</c>—, así que lo que cambia entre clases entra por delegado y
''' la ley queda escrita una sola vez.</para></summary>
Friend NotInheritable Class TomaDeBorrador(Of TD As Class)

    Private ReadOnly _buscar As Func(Of UInteger, TD)
    Private ReadOnly _registrar As Action(Of TD)
    Private ReadOnly _bajar As Action(Of UInteger)
    Private ReadOnly _idDe As Func(Of TD, UInteger)
    Private ReadOnly _construirBase As Func(Of UInteger, TD)

    Private _actual As TD
    Private _snapshot As TD
    Private _registroPrevio As TD
    Private _base As TD

    ''' <summary>La LÍNEA DE BASE PRISTINA del FormID tomado: el record tal como está en el archivo,
    ''' construido por la MISMA fábrica que arma el borrador override.
    ''' <para>Nothing cuando no hay record que leer (un borrador nuevo, cuyo FormID es provisional) o
    ''' cuando construirla falló ⇒ el volcado marca SUCIO, que es la dirección segura.</para></summary>
    Friend ReadOnly Property Base As TD
        Get
            Return _base
        End Get
    End Property

    Friend Sub New(buscar As Func(Of UInteger, TD), registrar As Action(Of TD),
                   bajar As Action(Of UInteger), idDe As Func(Of TD, UInteger),
                   construirBase As Func(Of UInteger, TD))
        If buscar Is Nothing Then Throw New ArgumentNullException(NameOf(buscar),
            "Sin buscador no hay reversión: no se puede saber si el registro que hay bajo el FormID es propio o ajeno.")
        If registrar Is Nothing Then Throw New ArgumentNullException(NameOf(registrar),
            "Sin registrador no hay reversión: lo que había registrado no se puede reponer.")
        If bajar Is Nothing Then Throw New ArgumentNullException(NameOf(bajar),
            "Sin baja, un borrador abandonado queda registrado para siempre y el guardado lo emite.")
        If idDe Is Nothing Then Throw New ArgumentNullException(NameOf(idDe),
            "Sin el FormID del borrador no hay nada que buscar ni que dar de baja.")
        If construirBase Is Nothing Then Throw New ArgumentNullException(NameOf(construirBase),
            "Sin línea de base, la suciedad se decidiría contra el estado de apertura — que es el defecto que esto cierra.")
        _buscar = buscar
        _registrar = registrar
        _bajar = bajar
        _idDe = idDe
        _construirBase = construirBase
    End Sub

    ''' <summary>Tomar el FormID del borrador que se va a editar. Corre EN EL MISMO GESTO que el snapshot
    ''' de apertura y ANTES del primer volcado.
    ''' <para>⛔ El orden es la mitad de la ley: el primer volcado REGISTRA el borrador nuevo, así que
    ''' capturar después haría que el editor se encontrara a SÍ MISMO como «registro previo» y no diera
    ''' de baja nunca — un borrador cancelado quedaría vivo y el guardado lo emitiría.</para>
    ''' <para>La base va bajo <c>Try</c> por lo mismo que el snapshot: construirla parsea y copia un record
    ''' del disco, eso puede tirar, esto corre desde manejadores sin <c>Try</c> y la app usa
    ''' <c>UnhandledExceptionMode.ThrowException</c> — un throw acá CIERRA la app. Sin base se marca sucio,
    ''' que es la dirección segura.</para></summary>
    Friend Sub Tomar(actual As TD, snapshotDeApertura As TD)
        _actual = actual
        _snapshot = snapshotDeApertura
        _registroPrevio = Nothing
        _base = Nothing
        If actual Is Nothing Then Return
        Dim fid = _idDe(actual)
        _registroPrevio = _buscar(fid)
        Try
            _base = _construirBase(fid)
        Catch ex As Exception
            _base = Nothing
            Logger.Log("TomaDeBorrador.Tomar (línea de base): " & ex.ToString())
        End Try
    End Sub

    ''' <summary>¿Quedó sucio el borrador después de volcarle los paneles? La LEY es
    ''' <see cref="Borradores.SucioContraLaBase"/>; acá sólo se le pasa si hay base, porque la comparación
    ''' la hace el llamador —es el único que sabe comparar SU tipo de borrador—.</summary>
    Friend Function Sucio(igualALaBase As Boolean) As Boolean
        Return Borradores.SucioContraLaBase(_base IsNot Nothing, igualALaBase)
    End Function

    ''' <summary>SOLTAR la toma sin aplicar la ley: el borrador tomado dejó de existir por decisión
    ''' EXPLÍCITA del usuario (el «Delete / Revert…» del selector).
    ''' <para>⛔ Abandonar acá REPONDRÍA lo que el usuario acaba de borrar: la toma sigue teniendo su
    ''' snapshot, así que la ley diría «restaurar» y el override revertido volvería al registro —por
    ''' encima del <c>MarkRecordForRemoval</c> que el mismo gesto acaba de poner— y el guardado lo
    ''' emitiría. Soltar y abandonar son gestos distintos y por eso son dos métodos.</para>
    ''' <para>Después de esto, <see cref="Abandonar"/> es un no-op hasta la próxima <see cref="Tomar"/>.</para></summary>
    Friend Sub Soltar()
        _actual = Nothing
        _snapshot = Nothing
        _registroPrevio = Nothing
        _base = Nothing
    End Sub

    ''' <summary>Abandonar sin aceptar: aplica <see cref="Borradores.QueHacerAlAbandonar"/>.</summary>
    Friend Sub Abandonar()
        If _actual Is Nothing Then Return
        Dim aRestaurar As TD = Nothing
        Select Case Borradores.QueHacerAlAbandonar(_registroPrevio, _actual, _snapshot, aRestaurar)
            Case Borradores.AccionAlAbandonar.DarDeBaja
                _bajar(_idDe(_actual))
            Case Borradores.AccionAlAbandonar.Restaurar
                _registrar(aRestaurar)
            Case Else
                ' NoTocar: adoptamos el objeto que ya estaba registrado y su snapshot no existe. Dejarlo
                ' mutado es mejor que destruirlo — ver el caso 3 de la ley.
        End Select
    End Sub

End Class
