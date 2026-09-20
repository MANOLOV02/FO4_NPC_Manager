Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>LA LECTURA DEL BYTE <c>Type</c> DE UNA CONDICIÓN (<c>CTDA</c>) y los textos con los que se
''' muestra una condición. Es transcripción, no diseño.
'''
''' <para>⛔⛔ <b>EL OPERADOR DE COMPARACIÓN TIENE SEIS VALORES, NO CINCO.</b> La tabla sale de
''' <c>3rd party references\TES5Edit\Core\wbDefinitionsCommon.pas:3487-3498</c> (la rama
''' <c>ctToStr, ctToSummary</c> de <c>wbConditionTypeToStr</c>, que es la que NOMBRA los valores) y el
''' validador de <c>:3477-3481</c> (<c>ctCheck</c>) whitelistea exactamente esos seis —
''' <c>0, 32, 64, 96, 128, 160</c>—, así que los únicos inválidos son <c>192</c> y <c>224</c>.</para>
'''
''' <para>⛔ <b>Esto ya se escribió mal una vez y quedó asentado para que no vuelva a pasar.</b> La
''' primera versión decía CINCO y declaraba el <c>32</c> «no es una combinación válida». Salía de leer
''' la rama <c>ctToEditValue</c> —el PINTADO del control— en vez del campo, 22 líneas más arriba de la
''' tabla que lo nombra. Y el corpus lo refuta con número: <b><c>32</c> = «Not Equal To» aparece en
''' 10.938 condiciones de Fallout 4 y 2.506 de Skyrim</b> (13.444 en total, el SEGUNDO operador más
''' usado), mientras que <c>192</c> y <c>224</c> no aparecen NUNCA. Los HDPT no lo mostraban porque sus
''' 40 condiciones tienen <c>Type = 0</c>: medir el byte sólo sobre HDPT era medir donde el caso es cero
''' por construcción.</para>
'''
''' <para>Las banderas son los bits bajos (<c>:3429-3435</c>): <c>1</c> Or · <c>2</c> Use Aliases ·
''' <c>4</c> Use Global · <c>8</c> Use Packdata · <c>16</c> Swap Subject and Target.</para></summary>
Friend Module CondicionesDeHeadPart

    ''' <summary>Máscara del operador: los tres bits altos del byte <c>Type</c>.</summary>
    Friend Const MascaraDeOperador As Byte = 224

    ''' <summary>Los SEIS operadores, con el nombre de xEdit. Orden de valor.</summary>
    Friend ReadOnly Operadores As New List(Of (Valor As Byte, Nombre As String, Simbolo As String)) From {
        (0, "Equal To", "=="),
        (32, "Not Equal To", "!="),
        (64, "Greater Than", ">"),
        (96, "Greater Than Or Equal To", ">="),
        (128, "Less Than", "<"),
        (160, "Less Than Or Equal To", "<=")}

    ''' <summary>Las CINCO banderas de los bits bajos, con el nombre de xEdit.</summary>
    Friend ReadOnly Banderas As New List(Of (Bit As Byte, Nombre As String)) From {
        (1, "Or"), (2, "Use Aliases"), (4, "Use Global"), (8, "Use Packdata"),
        (16, "Swap Subject and Target")}

    ''' <summary>Los nombres de <c>Run On</c> DEL JUEGO, por la sede del esquema.
    ''' <para>⛔⛔ Acá había una lista escrita a mano con OCHO entradas rotulada «del esquema
    ''' (<c>FmtFO4.F017</c>)». Eran las ocho de <b>Skyrim</b>: <c>FmtFO4.F017</c> tiene <b>ONCE</b>, y
    ''' medido sobre los dos corpus completos hay <b>177 CTDA de Fallout 4</b> con 8, 9 o 10 que esa lista
    ''' no podía representar. El detalle y el porqué están en
    ''' <see cref="Canon.CanonInterpretacion.NombresDeRunOnDeCondicion"/>. Es el TERCER campo de esta ola
    ''' con el mismo defecto de clase —<c>PNAM</c> 69/71 y el operador 32 fueron los otros dos— y este lo
    ''' introdujo el arreglo del segundo: una lista cerrada escrita a mano al lado de la tabla generada.</para></summary>
    Friend Function RunOnDe(game As Canon.WbGame) As List(Of (Valor As UInteger, Nombre As String))
        Dim r As New List(Of (Valor As UInteger, Nombre As String))
        For Each kv In Canon.CanonInterpretacion.NombresDeRunOnDeCondicion(game).OrderBy(Function(x) x.Key)
            r.Add((CUInt(kv.Key), kv.Value))
        Next
        Return r
    End Function

    ''' <summary>True si el operador del byte es uno de los seis que xEdit nombra. <c>192</c> y
    ''' <c>224</c> dan False: son los dos que su propio validador rechaza.</summary>
    Friend Function OperadorEsValido(tipo As Byte) As Boolean
        Dim op = CByte(tipo And MascaraDeOperador)
        Return Operadores.Any(Function(o) o.Valor = op)
    End Function

    Friend Function TieneBanderaOr(c As Canon.HdptFO4_Conditions) As Boolean
        If c Is Nothing Then Return False
        Return (c.ConditionType And 1) <> 0
    End Function

    ''' <summary>El nombre de la función de la condición, de la tabla generada del juego
    ''' (<c>WbConditions*.Nombres</c>, 479 entradas en FO4 y 402 en TES5, emitidas del <c>.pas</c>).
    ''' Un índice que la tabla no trae se muestra crudo en vez de en blanco.</summary>
    Friend Function NombreDeFuncion(c As Canon.HdptFO4_Conditions, game As Canon.WbGame) As String
        If c Is Nothing Then Return ""
        Return NombreDeFuncion(CInt(c.ConditionFunction), game)
    End Function

    Friend Function NombreDeFuncion(indice As Integer, game As Canon.WbGame) As String
        Dim nombre = Canon.CanonInterpretacion.NombreDeFuncionDeCondicion(indice, game)
        If String.IsNullOrEmpty(nombre) Then Return $"(function {indice})"
        Return nombre
    End Function

    ''' <summary>El texto del operador y su valor de comparación: «== 1.0» o «!= GLOB xxx».</summary>
    Friend Function TextoDeComparacion(c As Canon.HdptFO4_Conditions) As String
        If c Is Nothing Then Return ""
        Dim op = CByte(c.ConditionType And MascaraDeOperador)
        Dim simbolo = "?"
        For Each o In Operadores
            If o.Valor = op Then simbolo = o.Simbolo : Exit For
        Next
        If (c.ConditionType And 4) <> 0 Then
            ' Bandera «Use Global»: el valor de comparación es un GLOB, no un float.
            Return $"{simbolo} GLOB 0x{c.ConditionComparisonValueGlobal:X8}"
        End If
        Return $"{simbolo} {c.ConditionComparisonValueFloat.ToString(Globalization.CultureInfo.InvariantCulture)}"
    End Function

    ''' <summary>El nombre de <c>Run On</c> para la grilla. ⛔ Por CLAVE en la tabla del juego, no por
    ''' posición en una lista: la versión anterior indexaba una lista de ocho —las de Skyrim— y ni recibía
    ''' el juego, así que en Fallout 4 rotulaba mal los tres valores que sólo ese juego tiene y mostraba
    ''' «(8)» para <c>Command Target</c>. Los paréntesis quedan sólo para el valor que la tabla del juego
    ''' de verdad no nombra.</summary>
    Friend Function NombreDeRunOn(c As Canon.HdptFO4_Conditions, game As Canon.WbGame) As String
        If c Is Nothing Then Return ""
        Dim nombre As String = Nothing
        If Canon.CanonInterpretacion.NombresDeRunOnDeCondicion(game).TryGetValue(CLng(c.ConditionRunOn), nombre) Then
            Return nombre
        End If
        Return $"({c.ConditionRunOn})"
    End Function

    ''' <summary>El texto del parámetro 1 o 2. ⛔ El TIPO de cada parámetro lo decide la TABLA
    ''' (<c>WbConditions*.Params</c>) corregida por las banderas y el <c>Run On</c> —la MISMA función
    ''' que usa el lector, <c>WbDeciders.AdjustParam</c>— y no una lista escrita acá. Si el editor
    ''' tuviera su propia tabla habría dos leyes para «de qué tipo es este parámetro», y divergirían.
    ''' <para>Lo que se muestra es la rama que el árbol REALMENTE decodificó: se pregunta por los
    ''' <c>*Presente</c>, que es lo que el record trae, en vez de suponer por el tipo.</para></summary>
    Friend Function TextoDeParametro(c As Canon.HdptFO4_Conditions, cual As Integer,
                                     mainForm As MainForm) As String
        If c Is Nothing Then Return ""
        If cual = 1 Then
            If c.Parameter1QuestPresente Then Return mainForm.GetRecordDisplayNameForEditor(c.Parameter1Quest)
            If c.Parameter1QuestStagePresente Then Return c.Parameter1QuestStage.ToString()
            If c.Parameter1IntegerPresente Then Return c.Parameter1Integer.ToString()
            If c.Parameter1FloatPresente Then Return c.Parameter1Float.ToString(Globalization.CultureInfo.InvariantCulture)
            If c.Parameter1AliasPresente Then Return $"alias {c.Parameter1Alias}"
            Return ""
        End If
        If c.Parameter2QuestPresente Then Return mainForm.GetRecordDisplayNameForEditor(c.Parameter2Quest)
        If c.Parameter2QuestStagePresente Then Return c.Parameter2QuestStage.ToString()
        If c.Parameter2IntegerPresente Then Return c.Parameter2Integer.ToString()
        Return ""
    End Function

End Module
