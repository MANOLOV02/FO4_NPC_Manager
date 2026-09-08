Imports System.Collections
Imports System.Reflection

''' <summary>La igualdad POR VALOR, en un solo lugar, para los dos comparadores que la necesitan: la tabla de
''' canales de <see cref="PresetCategoryFilter"/> y el comparador de estados visuales.
'''
''' <para>⛔ Nace de una regla y de un defecto medido. La regla: escrita dos veces, una de las dos copias
''' termina en <c>Equals</c> — que compara REFERENCIAS y da VERDE con dos listas de contenido distinto. El
''' defecto: un comparador que no puede ver un campo lo daba por igual EN SILENCIO.</para>
'''
''' <para>⛔ El comparador de estados NO lleva lista de campos: <c>NPCVisualState</c> es todo campos públicos y
''' cero propiedades (censado), así que la reflexión los trae todos y un campo que alguien agregue mañana entra
''' solo. Es exactamente lo que <c>CloneVisualState</c> documenta como imposible con una lista a mano —
''' «un campo nuevo que no se agregue acá se pierde SILENCIOSAMENTE en TODO render».</para></summary>
Friend Module ComparacionPorValor

    ''' <summary>Igualdad POR VALOR de dos valores de campo.
    ''' <para>⛔ «Por valor» es POR ESTRUCTURA, no «por su texto». La primera versión comparaba
    ''' <c>Convert.ToString</c> y tenía una guarda que LANZABA cuando un tipo de referencia no traía
    ''' <c>ToString</c> propio — porque ahí el texto degrada al NOMBRE DEL TIPO y dos contenidos distintos
    ''' comparan iguales. La guarda estaba bien puesta; el atajo que custodiaba estaba mal elegido: hacía
    ''' reventar el predicado del COMMIT para cualquier preset de SSE con una head part sin resolver
    ''' (<c>List(Of JslotHeadPart)</c>). Comparar estructuralmente cierra las dos cosas — no afloja nada y
    ''' además elimina la CLASE del problema, que era «el próximo tipo sin ToString vuelve a reventar».</para>
    ''' <para>⛔ Las secuencias se comparan elemento a elemento y RECURSIVAMENTE: en este árbol hay listas de
    ''' listas, y una comparación de un nivel da verde con elementos distintos.</para>
    ''' <para>⛔ Un tipo que define SU PROPIA igualdad (override de <c>Equals</c>) manda: es él quien sabe qué
    ''' significa «igual» para sus datos.</para></summary>
    Friend Function IgualPorValor(a As Object, b As Object) As Boolean
        Return IgualPorValor(a, b, 0, "", New List(Of Tuple(Of Object, Object))())
    End Function

    ''' <summary>Tope de anidamiento. ⛔ Cuando se pasa se LANZA, no se corta: truncar daría «iguales» sobre
    ''' dos grafos que nadie termino de mirar, que es justo el verde en vacío que este módulo evita. El valor
    ''' es holgado para los datos de este árbol (preset → lista → struct → array).</summary>
    Private Const PROFUNDIDAD_MAXIMA As Integer = 16

    ''' <param name="ruta">⛔⛔ EL CAMINO recorrido hasta aca, para que el tope de anidamiento se
    ''' pueda JUZGAR. Con solo el tipo del fondo -- «paso los 16 niveles comparando System.String[]»-- no se
    ''' distingue un ciclo (donde el tope es correcto y hay que cortar la rama) de un dato genuinamente
    ''' profundo (donde el tope esta corto). Son cosas opuestas y el arreglo de una empeora la otra.</param>
    ''' <param name="camino">⛔⛔ LOS PARES YA ABIERTOS, para reconocer un CICLO.
    ''' <para>El esquema de records es un grafo ciclico -- `Def.Members` vuelve a su `DefParent`-- y las
    ''' vistas canonicas lo exponen como propiedad publica. Comparando `ObjectTemplateCombinations` el
    ''' recorrido daba vueltas por `Members[0].DefParent.Members[0].DefParent...` hasta chocar el tope, y
    ''' eso hacia REVENTAR a 1134 de 4365 NPC de Fallout.</para>
    ''' <para>⛔ Se corta por IDENTIDAD DEL PAR, no subiendo el tope: si este mismo par (a, b) ya esta
    ''' abierto mas arriba, seguir no agrega informacion -- la respuesta la decide la llamada de afuera.
    ''' Subir el tope solo habria movido el choque unos niveles mas alla; y una lista de tipos a evitar
    ''' seria una segunda ley que mantener. El tope queda igual, de red por si aparece otra forma.</para></param>
    Private Function IgualPorValor(a As Object, b As Object, nivel As Integer, ruta As String,
                                   camino As List(Of Tuple(Of Object, Object))) As Boolean
        If a Is Nothing AndAlso b Is Nothing Then Return True
        If a Is Nothing OrElse b Is Nothing Then Return False
        ' ⛔⛔ LA MISMA INSTANCIA ES IGUAL A SI MISMA, y cortar aca no es un atajo: es cierto para
        ' cualquier grafo. Lo que resuelve es concreto -- las vistas canonicas exponen el ESQUEMA del record
        ' como propiedad publica, y el esquema es metadato COMPARTIDO: los dos lados apuntan al MISMO objeto.
        ' Sin este corte, comparar `ObjectTemplateCombinations` se iba a recorrer el esquema entero
        ' (`.Node.Def.DefParent.Members[12].Element.Members[2]...EnumValues[0].Key`) y reventaba en 1134 de
        ' 4365 NPC de Fallout. No se compara el esquema porque no es dato del NPC: es la definicion del
        ' formato, identica por construccion.
        If Not a.GetType().IsValueType AndAlso ReferenceEquals(a, b) Then Return True
        If nivel > PROFUNDIDAD_MAXIMA Then
            Throw New InvalidOperationException(
                "IgualPorValor paso los " & PROFUNDIDAD_MAXIMA & " niveles comparando " & a.GetType().FullName &
                ". CAMINO: " & ruta &
                " | o hay un ciclo, o el dato es mas profundo de lo previsto. Se LANZA en vez de truncar, " &
                "porque truncar daria IGUALES sobre dos grafos que nadie termino de mirar.")
        End If

        Dim ta = a.GetType(), tb = b.GetType()
        If ta IsNot tb Then Return False

        ' ⛔ El ciclo se reconoce ANTES de bajar otro nivel. Solo para tipos de referencia: dos enteros
        ' iguales no son "el mismo nodo".
        If Not ta.IsValueType Then
            For Each par In camino
                If ReferenceEquals(par.Item1, a) AndAlso ReferenceEquals(par.Item2, b) Then Return True
            Next
            camino.Add(Tuple.Create(a, b))
        End If
        Try

        ' Escalares y todo lo que ya sabe compararse solo.
        If ta.IsPrimitive OrElse ta.IsEnum OrElse TypeOf a Is String OrElse TypeOf a Is Decimal OrElse
           TypeOf a Is DateTime OrElse TypeOf a Is TimeSpan OrElse TypeOf a Is Guid OrElse
           TypeOf a Is Drawing.Color Then
            Return Object.Equals(a, b)
        End If

        Dim ea = TryCast(a, IEnumerable)
        Dim eb = TryCast(b, IEnumerable)
        If ea IsNot Nothing AndAlso eb IsNot Nothing Then
            Dim la = ea.Cast(Of Object)().ToList()
            Dim lb = eb.Cast(Of Object)().ToList()
            If la.Count <> lb.Count Then Return False
            For i = 0 To la.Count - 1
                If Not IgualPorValor(la(i), lb(i), nivel + 1, ruta & "[" & i & "]", camino) Then Return False
            Next
            Return True
        End If

        ' ⛔ El tipo que DEFINE su propia igualdad manda: es él quien sabe qué significa «igual» para sus
        ' datos. Se detecta por el `DeclaringType` del override, no por si `Equals` existe — existe siempre.
        Dim eq = ta.GetMethod("Equals", New Type() {GetType(Object)})
        If eq IsNot Nothing AndAlso eq.DeclaringType IsNot GetType(Object) AndAlso
           eq.DeclaringType IsNot GetType(ValueType) Then
            Return Object.Equals(a, b)
        End If

        ' Estructural: todos los campos públicos y las propiedades legibles SIN índice. Las indexadas se
        ' saltean a propósito — no se puede enumerar su dominio, y `Item(i)` de una lista ya viaja por la
        ' rama de secuencia de arriba.
        Dim miro As Boolean = False
        For Each f In ta.GetFields(BindingFlags.Public Or BindingFlags.Instance)
            miro = True
            If Not IgualPorValor(f.GetValue(a), f.GetValue(b), nivel + 1, ruta & "." & f.Name, camino) Then Return False
        Next
        For Each pr In ta.GetProperties(BindingFlags.Public Or BindingFlags.Instance)
            If Not pr.CanRead OrElse pr.GetIndexParameters().Length > 0 Then Continue For
            miro = True
            If Not IgualPorValor(pr.GetValue(a, Nothing), pr.GetValue(b, Nothing), nivel + 1, ruta & "." & pr.Name, camino) Then Return False
        Next
        If Not miro Then
            ' ⛔ Un tipo de referencia sin UN SOLO miembro público legible y sin `Equals` propio no se puede
            ' comparar por valor, y darlo por igual sería la misma ceguera de antes con otro disfraz.
            Throw New InvalidOperationException(
                "IgualPorValor no puede comparar " & ta.FullName & ": no tiene Equals propio ni un solo " &
                "miembro publico legible. Darlo por igual seria comparar dos objetos que nadie miro.")
        End If
        Return True
        Finally
            If Not ta.IsValueType AndAlso camino.Count > 0 Then camino.RemoveAt(camino.Count - 1)
        End Try
    End Function

    ''' <summary>El NOMBRE del primer campo que difiere, o Nothing si son iguales. Es la forma que usa un gate:
    ''' un booleano pelado obliga a re-recorrer a mano para poder decir QUÉ se rompió, y esa segunda pasada
    ''' escrita aparte es una segunda ley.</summary>
    ''' <summary>⛔⛔ CAMPOS QUE SON LA ENTRADA, NO UN VALOR DEL ESTADO. `RecordBase` es el record
    ''' con el que se dibuja: un GRAFO entero, no un dato comparable. Compararlo por valor recorre el record
    ''' completo y revienta a los 16 niveles de profundidad -- y eso ya paso: el caso que controla las
    ''' excepciones del comparador reventaba en 6452 de 6452 sujetos y, como su `Catch` era mudo, imprimia
    ''' "difiere en 0 NPC = EXCEPCION MUERTA" sobre CERO mediciones.
    ''' <para>⛔ Vive ACA y no en cada gate: una lista de exclusiones copiada es una lista que diverge, que
    ''' es justo lo que dice el comentario de las excepciones de mas abajo.</para>
    ''' <para>⛔ Y tiene CONTROL: si el campo desaparece del estado, `ControlarEntradas` avisa. Una
    ''' exclusion sin control es el escondite del proximo defecto.</para></summary>
    Friend ReadOnly ENTRADAS_NO_VALORES As String() = {"RecordBase"}

    ''' <summary>Devuelve el nombre de la entrada declarada que el estado YA NO TIENE, o Nothing. Lo llama el
    ''' gate: sin esto, el dia que `RecordBase` se renombre la exclusion queda tapando un campo que no existe
    ''' y el comparador vuelve a comparar el grafo entero sin que nadie se entere.</summary>
    Friend Function ControlarEntradas() As String
        Dim todos = GetType(MainForm.NPCVisualState).GetFields(BindingFlags.Public Or BindingFlags.Instance).
                    Select(Function(f) f.Name).ToList()
        For Each nombre In ENTRADAS_NO_VALORES
            If Not todos.Contains(nombre) Then Return nombre
        Next
        Return Nothing
    End Function

    Friend Function PrimerCampoDistinto(a As MainForm.NPCVisualState,
                                        b As MainForm.NPCVisualState,
                                        excepto As IEnumerable(Of String)) As String
        If a Is Nothing OrElse b Is Nothing Then
            Return If(a Is b, Nothing, "(uno de los dos estados es Nothing)")
        End If
        Dim saltear As New HashSet(Of String)(If(excepto, Enumerable.Empty(Of String)()), StringComparer.Ordinal)
        For Each nombre In ENTRADAS_NO_VALORES
            saltear.Add(nombre)
        Next
        For Each f In GetType(MainForm.NPCVisualState).GetFields(BindingFlags.Public Or BindingFlags.Instance)
            If saltear.Contains(f.Name) Then Continue For
            Try
                If Not IgualPorValor(f.GetValue(a), f.GetValue(b)) Then Return f.Name
            Catch ex As InvalidOperationException
                ' ⛔⛔ EL LIMITE DE PROFUNDIDAD TIENE QUE DECIR EN QUE CAMPO SALTO. Sin el nombre, el
                ' llamador ve "el comparador reventó" y no puede juzgar si el limite esta corto o si hay un
                ' ciclo de verdad -- que son cosas opuestas. Se re-lanza con el campo adelante.
                Throw New InvalidOperationException($"campo `{f.Name}`: {ex.Message}", ex)
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>Los CINCO campos que DEBEN diferir entre la sede del render y la del bake, y por qué. Vive acá
    ''' y no en cada gate porque una lista de excepciones copiada es una lista que diverge.
    ''' <list type="bullet">
    ''' <item><c>ModelSourceFormID</c> — el bake se lo asigna DESPUÉS de proyectar; el render lo deja en 0.</item>
    ''' <item><c>DefaultOutfitFormID</c> / <c>SleepOutfitFormID</c> — el render sale de la cadena de Inventory,
    ''' el bake de <c>CreateOwnInventoryState</c>.</item>
    ''' <item><c>TraitsSourceFormID</c> — render = el terminal, bake = sí mismo.</item>
    ''' </list>
    ''' <para>⛔ <c>VariantLabel</c> e <c>InventorySourceFormID</c> estaban acá y SALIERON: están declarados y
    ''' NUNCA ASIGNADOS — por ninguna de las dos sedes — así que no es que «deban diferir», es que valen lo
    ''' mismo siempre. Los cazó el control de excepciones muertas del gate, y sacarlos es lo correcto: una
    ''' excepción que no tapa nada hoy tapa algo mañana, el día que alguien asigne uno de los dos en UNA sola
    ''' sede. Compararlos es gratis y cierra esa puerta.</para>
    ''' <para>Las cuatro que quedan difieren DE VERDAD, con su cuenta medida (sse/fo4):
    ''' <c>ModelSourceFormID</c> 6452/3231 · <c>TraitsSourceFormID</c> 2457/1429 ·
    ''' <c>DefaultOutfitFormID</c> 919/84 · <c>SleepOutfitFormID</c> 176/10.</para></summary>
    Friend ReadOnly DebenDiferirEntreRenderYBake As String() = {
        "ModelSourceFormID", "DefaultOutfitFormID", "SleepOutfitFormID", "TraitsSourceFormID"
    }

End Module
