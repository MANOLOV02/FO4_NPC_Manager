Imports FO4_Base_Library

''' <summary>Qué representa una fila del árbol de NPC. Es lo que decide la sangría, el glifo de
''' expandir y qué se puede seleccionar.</summary>
Public Enum TipoDeFila
    ''' <summary>Cabecera de plugin de la sección 1 ("[00042] MiMod.esp (23)").</summary>
    GrupoDePlugin
    ''' <summary>NPC colocado, hijo de un grupo de plugin.</summary>
    Npc
    ''' <summary>Cabecera de plugin de la sección 2 ("[00042] [LVLN] MiMod.esp (7)").</summary>
    GrupoDeLvlnPorPlugin
    ''' <summary>Una leveled list, hija de un grupo de la sección 2.</summary>
    Lvln
    ''' <summary>NPC alcanzable desde una leveled list, hijo de un <see cref="Lvln"/>.</summary>
    NpcDeLvln
End Enum

''' <summary>Una fila del árbol.
'''
''' <para>Reemplaza al <c>TreeNode</c> como unidad del modelo, y esa es toda la diferencia de costo:
''' un <c>TreeNode</c> es un ítem Win32 con su handle —medido: ~0,28 ms entre crearlo y destruirlo—,
''' mientras que esto es un objeto de ~60 bytes. Con 7.000 filas eso es 1.960 ms contra menos de uno.
''' El control sólo materializa las ~30 filas que se ven.</para>
'''
''' <para><see cref="Clave"/> es el MISMO identificador que llevaba <c>TreeNode.Name</c>
''' (<c>NPC_0001A2B3</c>, <c>PLUGIN_MiMod.esp</c>, …), así que lo que buscaba por nombre —la
''' re-selección después de guardar— sigue buscando por lo mismo.</para></summary>
Public NotInheritable Class FilaDeArbol

    Public ReadOnly Property Tipo As TipoDeFila
    Public ReadOnly Property Clave As String
    Public ReadOnly Property Tag As Object

    ''' <summary>Texto que se dibuja. Mutable porque la cuenta de la cabecera ("(23)") se conoce recién
    ''' cuando se terminaron de agregar los hijos, igual que antes con <c>TreeNode.Text</c>.</summary>
    Public Property Texto As String

    ''' <summary>0 = raíz. Es lo único que gobierna la sangría; el control no recorre padres para
    ''' calcularla.</summary>
    Public ReadOnly Property Nivel As Integer

    Public ReadOnly Property Hijos As New List(Of FilaDeArbol)
    Public Property Padre As FilaDeArbol

    ''' <summary>Si la fila está desplegada. La escribe el modelo, no el control: el control pide
    ''' <see cref="ModeloDeArbol.Alternar"/> y vuelve a leer la lista aplanada.</summary>
    Public Property Expandida As Boolean

    ''' <summary>Si es el ULTIMO de sus hermanos. Lo necesita el dibujo de las lineas de jerarquia: la
    ''' vertical de un nivel se corta en la ultima rama y sigue en las demas. Lo calcula el modelo al
    ''' aplanar, una vez por fila, en vez de que el pintado pregunte por el indice dentro del padre —
    ''' que seria una busqueda lineal por CADA fila dibujada y por cada nivel de profundidad.</summary>
    Public Property EsUltimoHermano As Boolean

    Public ReadOnly Property TieneHijos As Boolean
        Get
            Return Hijos.Count > 0
        End Get
    End Property

    Public Sub New(tipo As TipoDeFila, clave As String, texto As String, nivel As Integer, tag As Object)
        _Tipo = tipo
        _Clave = clave
        _Texto = texto
        _Nivel = nivel
        _Tag = tag
    End Sub

    Public Function Agregar(hija As FilaDeArbol) As FilaDeArbol
        hija.Padre = Me
        Hijos.Add(hija)
        Return hija
    End Function

    ''' <summary>Si esta fila cuelga de <paramref name="posible"/>, a cualquier profundidad.
    ''' <para>Lo necesita el colapso: cerrar un grupo no puede dejar el foco ADENTRO de lo que se cierra,
    ''' y eso hay que decidirlo ANTES de aplanar, que es cuando todavía se puede saber quién colgaba de
    ''' quién. El árbol tiene tres niveles, así que subir por <see cref="Padre"/> no necesita caché.</para></summary>
    Public Function CuelgaDe(posible As FilaDeArbol) As Boolean
        If posible Is Nothing Then Return False
        Dim p = Padre
        While p IsNot Nothing
            If p Is posible Then Return True
            p = p.Padre
        End While
        Return False
    End Function

    ''' <summary>El NPC de esta fila, o Nothing. Las dos clases de fila que llevan NPC
    ''' (<see cref="TipoDeFila.Npc"/> y <see cref="TipoDeFila.NpcDeLvln"/>) se preguntan igual, para que
    ''' nadie tenga que acordarse de mirar las dos.</summary>
    Public ReadOnly Property Npc As NPC_Data
        Get
            Return TryCast(Tag, NPC_Data)
        End Get
    End Property

End Class

''' <summary>El árbol de NPC como DATOS, sin control de por medio: la jerarquía completa y la lista
''' aplanada de lo que está visible.
'''
''' <para>Separarlo del control es lo que hace que el repoblado sea barato y que se pueda probar sin
''' pantalla: armar el modelo no toca Win32 ni una vez, y expandir o colapsar es volver a aplanar —
''' medido en 4 ms para 4.543 filas—.</para></summary>
Public NotInheritable Class ModeloDeArbol

    Public ReadOnly Property Raices As New List(Of FilaDeArbol)

    ''' <summary>Las filas que se ven, en orden. Es lo que el control indexa por número de fila, así que
    ''' su orden ES el orden de la pantalla.</summary>
    Public ReadOnly Property Visibles As New List(Of FilaDeArbol)

    ''' <summary>Fila por clave, para poder resolver "seleccioná el NPC 0001A2B3" sin recorrer nada.
    ''' Una clave puede repetirse —el MISMO NPC aparece bajo su plugin y bajo cada leveled list que lo
    ''' lista, que es una regla del producto, no un descuido— y en ese caso gana la primera, que es la
    ''' de la sección 1. Es la que encontraba <c>Nodes.Find</c> antes.</summary>
    Private ReadOnly _porClave As New Dictionary(Of String, FilaDeArbol)(StringComparer.Ordinal)

    Public Sub Limpiar()
        Raices.Clear()
        Visibles.Clear()
        _porClave.Clear()
    End Sub

    Public Function AgregarRaiz(fila As FilaDeArbol) As FilaDeArbol
        Raices.Add(fila)
        Return fila
    End Function

    ''' <summary>Rehace <see cref="Visibles"/> a partir de la jerarquía y del estado de expansión.
    ''' <para>Recorre con una pila explícita y no por recursión: la profundidad es chica (tres niveles),
    ''' pero así el costo es una sola pasada por fila visible y no hay marcos de pila por nodo.</para></summary>
    Public Sub Aplanar()
        Visibles.Clear()
        _porClave.Clear()
        For i = 0 To Raices.Count - 1
            Raices(i).EsUltimoHermano = (i = Raices.Count - 1)
        Next
        For Each raiz In Raices
            Indexar(raiz)
            AplanarDesde(raiz)
        Next
    End Sub

    ''' <summary>Registra la fila y TODA su descendencia en el índice por clave, mire o no la expansión.
    ''' <para>⛔ Indexar sólo lo visible fue un defecto real, y lo encontró el gate de render: buscar por
    ''' clave algo que está dentro de un grupo cerrado devolvía Nothing, que es EXACTAMENTE el caso para el
    ''' que existe la búsqueda — re-seleccionar un NPC después de guardarlo, cuando el árbol se acaba de
    ''' repoblar y su grupo puede estar cerrado. El índice describe el ÁRBOL; la expansión sólo decide qué
    ''' se dibuja.</para></summary>
    Private Sub Indexar(fila As FilaDeArbol)
        If Not _porClave.ContainsKey(fila.Clave) Then _porClave(fila.Clave) = fila
        For i = 0 To fila.Hijos.Count - 1
            fila.Hijos(i).EsUltimoHermano = (i = fila.Hijos.Count - 1)
        Next
        For Each hija In fila.Hijos
            Indexar(hija)
        Next
    End Sub

    Private Sub AplanarDesde(fila As FilaDeArbol)
        Visibles.Add(fila)
        If Not fila.Expandida Then Return
        For Each hija In fila.Hijos
            AplanarDesde(hija)
        Next
    End Sub

    ''' <summary>Expande o colapsa una fila y devuelve si hubo cambio. No re-aplana: eso lo hace el
    ''' control, que además tiene que avisarle a Windows el tamaño nuevo.</summary>
    Public Function Alternar(fila As FilaDeArbol) As Boolean
        If fila Is Nothing OrElse Not fila.TieneHijos Then Return False
        fila.Expandida = Not fila.Expandida
        Return True
    End Function

    Public Function PorClave(clave As String) As FilaDeArbol
        If String.IsNullOrEmpty(clave) Then Return Nothing
        Dim f As FilaDeArbol = Nothing
        If _porClave.TryGetValue(clave, f) Then Return f
        Return Nothing
    End Function

    ''' <summary>Abre todos los ancestros de una fila para que quede visible, y devuelve si movió algo.
    ''' Es lo que hace falta antes de seleccionar por clave algo que está dentro de un grupo cerrado —
    ''' el caso de la re-selección después de guardar.</summary>
    Public Function AbrirAncestros(fila As FilaDeArbol) As Boolean
        Dim cambio = False
        Dim p = fila?.Padre
        While p IsNot Nothing
            If Not p.Expandida Then
                p.Expandida = True
                cambio = True
            End If
            p = p.Padre
        End While
        Return cambio
    End Function

    ''' <summary>Índice de una fila dentro de <see cref="Visibles"/>, o -1. Lineal a propósito: se usa
    ''' una vez por selección, no por fila dibujada.</summary>
    Public Function IndiceVisible(fila As FilaDeArbol) As Integer
        If fila Is Nothing Then Return -1
        For i = 0 To Visibles.Count - 1
            If Visibles(i) Is fila Then Return i
        Next
        Return -1
    End Function

End Class
