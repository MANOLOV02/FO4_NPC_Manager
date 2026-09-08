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
        Return IgualPorValor(a, b, 0)
    End Function

    ''' <summary>Tope de anidamiento. ⛔ Cuando se pasa se LANZA, no se corta: truncar daría «iguales» sobre
    ''' dos grafos que nadie termino de mirar, que es justo el verde en vacío que este módulo evita. El valor
    ''' es holgado para los datos de este árbol (preset → lista → struct → array).</summary>
    Private Const PROFUNDIDAD_MAXIMA As Integer = 16

    Private Function IgualPorValor(a As Object, b As Object, nivel As Integer) As Boolean
        If a Is Nothing AndAlso b Is Nothing Then Return True
        If a Is Nothing OrElse b Is Nothing Then Return False
        If nivel > PROFUNDIDAD_MAXIMA Then
            Throw New InvalidOperationException(
                "IgualPorValor paso los " & PROFUNDIDAD_MAXIMA & " niveles comparando " & a.GetType().FullName &
                ": o hay un ciclo, o el dato es mas profundo de lo previsto. Se LANZA en vez de truncar, " &
                "porque truncar daria IGUALES sobre dos grafos que nadie termino de mirar.")
        End If

        Dim ta = a.GetType(), tb = b.GetType()
        If ta IsNot tb Then Return False

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
                If Not IgualPorValor(la(i), lb(i), nivel + 1) Then Return False
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
            If Not IgualPorValor(f.GetValue(a), f.GetValue(b), nivel + 1) Then Return False
        Next
        For Each pr In ta.GetProperties(BindingFlags.Public Or BindingFlags.Instance)
            If Not pr.CanRead OrElse pr.GetIndexParameters().Length > 0 Then Continue For
            miro = True
            If Not IgualPorValor(pr.GetValue(a, Nothing), pr.GetValue(b, Nothing), nivel + 1) Then Return False
        Next
        If Not miro Then
            ' ⛔ Un tipo de referencia sin UN SOLO miembro público legible y sin `Equals` propio no se puede
            ' comparar por valor, y darlo por igual sería la misma ceguera de antes con otro disfraz.
            Throw New InvalidOperationException(
                "IgualPorValor no puede comparar " & ta.FullName & ": no tiene Equals propio ni un solo " &
                "miembro publico legible. Darlo por igual seria comparar dos objetos que nadie miro.")
        End If
        Return True
    End Function

    ''' <summary>El NOMBRE del primer campo que difiere, o Nothing si son iguales. Es la forma que usa un gate:
    ''' un booleano pelado obliga a re-recorrer a mano para poder decir QUÉ se rompió, y esa segunda pasada
    ''' escrita aparte es una segunda ley.</summary>
    Friend Function PrimerCampoDistinto(a As MainForm.NPCVisualState,
                                        b As MainForm.NPCVisualState,
                                        excepto As IEnumerable(Of String)) As String
        If a Is Nothing OrElse b Is Nothing Then
            Return If(a Is b, Nothing, "(uno de los dos estados es Nothing)")
        End If
        Dim saltear As New HashSet(Of String)(If(excepto, Enumerable.Empty(Of String)()), StringComparer.Ordinal)
        For Each f In GetType(MainForm.NPCVisualState).GetFields(BindingFlags.Public Or BindingFlags.Instance)
            If saltear.Contains(f.Name) Then Continue For
            If Not IgualPorValor(f.GetValue(a), f.GetValue(b)) Then Return f.Name
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
