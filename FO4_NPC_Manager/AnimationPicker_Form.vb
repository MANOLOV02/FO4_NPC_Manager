Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Picker de animaciones en ÁRBOL: Role (Locomoción/Arma/Furniture/Idle/Pipboy/Core) → CARPETA anidada
''' (la taxonomía de assets de Bethesda: Weapon\Pistol, 1HM, 2HM, MT\Neutral, Furniture\… — la carpeta resuelta del
''' clip, que elige el SAPT del subgraph) → clip. Filtro de texto + "Filter by gender" (default ON: varón oculta
''' female-only) + "Show 1st-person/camera" (default OFF: oculta los clips alcanzables SOLO vía subgraphs de 1ª
''' persona = cámara/viewmodel). Hoja con insignia ⊕ = aditivo (overlay, no pose). Los clips sourced de records IDLE
''' (Category &lt;&gt; "") NO van al árbol Role/carpeta: tienen su propia rama "Gestures &amp; Dialogue (IDLE)" agrupada por
''' Category (= el evento ENAM del record: dyn_Talk, dyn_Flavor, TurnStart…). Lo abre "Select Animation".</summary>
Public Class AnimationPicker_Form

    Private ReadOnly _all As List(Of ResolvedAnimationClip)
''' <summary>Clip que estaba elegido al abrir. ⛔ Se guarda la REFERENCIA, no el path: desde que el
''' enumerador separa variantes (mismo .hkx con distinto crop/speed/ping-pong), matchear por
''' AnimationFile preseleccionaba la PRIMERA del arbol y el caller saltaba a otra variante sin que el
''' usuario hubiera elegido nada.</summary>
    Private ReadOnly _initialClip As ResolvedAnimationClip
    Private ReadOnly _isFemale As Boolean

    ' Pinceles cacheados: el owner-draw corre por nodo VISIBLE en cada repintado, asi que no se asigna
    ' nada por fila. (OwnerDrawText, no OwnerDrawAll: Windows sigue dibujando fondo y +/-.)
    Private ReadOnly _brNombre As New Drawing.SolidBrush(Drawing.SystemColors.WindowText)
    Private ReadOnly _brRuta As New Drawing.SolidBrush(Drawing.Color.FromArgb(140, 140, 140))
    Private ReadOnly _brVariante As New Drawing.SolidBrush(Drawing.Color.FromArgb(196, 112, 0))
    Private ReadOnly _brInsignia As New Drawing.SolidBrush(Drawing.Color.FromArgb(72, 128, 190))
    Private ReadOnly _brConteo As New Drawing.SolidBrush(Drawing.Color.FromArgb(150, 150, 150))
    Private ReadOnly _brSel As New Drawing.SolidBrush(Drawing.SystemColors.Highlight)
    Private ReadOnly _brFondo As New Drawing.SolidBrush(Drawing.SystemColors.Window)
    Private _fuenteNegrita As Drawing.Font

    ''' <summary>Clip elegido (Nothing si se canceló o si el nodo seleccionado es una categoría).</summary>
    Public ReadOnly Property SelectedClip As ResolvedAnimationClip
        Get
            Return TryCast(TreeClips.SelectedNode?.Tag, ResolvedAnimationClip)
        End Get
    End Property

    Public Sub New(clips As IEnumerable(Of ResolvedAnimationClip), isFemale As Boolean, Optional currentClip As ResolvedAnimationClip = Nothing)
        InitializeComponent()
        _all = If(clips, Enumerable.Empty(Of ResolvedAnimationClip)()).Where(Function(c) c IsNot Nothing).ToList()
        _isFemale = isFemale
        _initialClip = currentClip
        _fuenteNegrita = New Drawing.Font(TreeClips.Font, Drawing.FontStyle.Bold)
    End Sub

    ''' <summary>Libera los objetos GDI+ del owner-draw: los 7 <c>SolidBrush</c> de :27-33 y la
    ''' <c>Font</c> de :48.
    ''' <para>⛔ NO PUEDEN IR EN <c>components</c>: ni <c>SolidBrush</c> ni <c>Font</c> implementan
    ''' <c>IComponent</c> (comprobado: <c>components.Add</c> ni compilaría), y el <c>Dispose</c> generado
    ''' (<c>AnimationPicker_Form.Designer.vb:12-20</c>) sólo toca <c>components</c> — así que los ocho
    ''' quedaban a merced del finalizador, uno por cada apertura del selector.</para>
    ''' <para>⛔ Y TAMPOCO UN SEGUNDO <c>Dispose</c>: el Designer ya sobrescribe
    ''' <c>Dispose(disposing As Boolean)</c> y dos <c>Overrides</c> con la misma firma dan
    ''' <c>BC30269</c> (comprobado compilando). Un handler de cierre es el idioma del repo — lo mismo
    ''' hace <c>MainForm</c> con <c>_multiSelectBrush</c> / <c>_dirtyNodeFont</c> / <c>_deleteNodeFont</c>.</para>
    ''' <para>En <c>FormClosed</c> y NO en <c>FormClosing</c>: este último es CANCELABLE, y un cierre
    ''' cancelado dejaría el formulario vivo con los pinceles muertos y <c>TreeClips_DrawNode</c> tirando
    ''' <c>ArgumentException</c> en el primer repintado. Además el formulario se abre siempre con
    ''' <c>Using</c> + <c>ShowDialog</c> y nunca se re-muestra.</para></summary>
    Private Sub AnimationPicker_Form_FormClosed(sender As Object, e As FormClosedEventArgs) Handles Me.FormClosed
        _brNombre.Dispose() : _brRuta.Dispose() : _brVariante.Dispose() : _brInsignia.Dispose()
        _brConteo.Dispose() : _brSel.Dispose() : _brFondo.Dispose()
        _fuenteNegrita?.Dispose()
    End Sub

    ' ⛔ El TreeView NO expone DoubleBuffered (es Protected en Control) y en OwnerDraw eso se ve: la fila
    ' de arriba queda con basura de colores hasta que algo la repinta. Se activa el doble buffer NATIVO del
    ' control comun (TVS_EX_DOUBLEBUFFER), que es la via documentada y no toca miembros protegidos por
    ' reflexion. Tiene que ir DESPUES de que exista el handle: antes, SendMessage no llega a ningun lado.
    Private Const TVM_SETEXTENDEDSTYLE As Integer = &H112C   ' TV_FIRST (&H1100) + 44
    Private Const TVS_EX_DOUBLEBUFFER As Integer = &H4

    <Runtime.InteropServices.DllImport("user32.dll", CharSet:=Runtime.InteropServices.CharSet.Auto)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Sub TreeClips_HandleCreated(sender As Object, e As EventArgs) Handles TreeClips.HandleCreated
        SendMessage(TreeClips.Handle, TVM_SETEXTENDEDSTYLE, New IntPtr(TVS_EX_DOUBLEBUFFER), New IntPtr(TVS_EX_DOUBLEBUFFER))
    End Sub

    Private Sub AnimationPicker_Form_Load(sender As Object, e As EventArgs) Handles Me.Load
        ' La forma entera tambien: el panel de filtros parpadeaba al abrir por el mismo motivo.
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint, True)
        Me.UpdateStyles()
    End Sub

    Private Sub AnimationPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Rebuild()
        TextFilter.Focus()
    End Sub

    Private Sub TextFilter_TextChanged(sender As Object, e As EventArgs) Handles TextFilter.TextChanged
        Rebuild()
    End Sub

    Private Sub CheckFilterGender_CheckedChanged(sender As Object, e As EventArgs) Handles CheckFilterGender.CheckedChanged
        Rebuild()
    End Sub

    Private Sub CheckShow1stPerson_CheckedChanged(sender As Object, e As EventArgs) Handles CheckShow1stPerson.CheckedChanged
        Rebuild()
    End Sub

    Private Sub TreeClips_DoubleClick(sender As Object, e As EventArgs) Handles TreeClips.DoubleClick
        If SelectedClip IsNot Nothing Then
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub

    Private Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        If SelectedClip Is Nothing Then
            MsgBox("Select an animation (a leaf node) from the tree.", vbInformation Or vbOKOnly, "Select Animation")
            Return
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' Reconstruye el árbol: filtros (texto AND de términos vs nombre+role+axis+file; género; 1ª persona) → agrupa por
    ' Role → carpeta anidada → clip. Un clip con varios roles aparece bajo cada Role que le corresponde. Los clips con
    ' Category <> "" (sourced de records IDLE) se desvían a una rama propia "Gestures & Dialogue (IDLE)" agrupada por
    ' su Category (= evento ENAM del record), en vez de ensuciar el árbol Role/carpeta.
    Private Sub Rebuild()
        ' Guard: el Designer setea CheckFilterGender.Checked=True dentro de InitializeComponent() → dispara
        ' CheckedChanged → Rebuild() ANTES de que el ctor asigne _all. El build real corre en Shown.
        If _all Is Nothing Then Return
        Dim terms = TextFilter.Text.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim shown = _all.Where(Function(c) PassesGender(c) AndAlso PassesPerspective(c) AndAlso MatchesAll(c, terms)).ToList()
        Dim shownNormal = shown.Where(Function(c) String.IsNullOrEmpty(c.Category)).ToList()
        Dim shownGesture = shown.Where(Function(c) Not String.IsNullOrEmpty(c.Category)).ToList()

        TreeClips.BeginUpdate()
        Try
            TreeClips.Nodes.Clear()
            Dim toSelect As TreeNode = Nothing
            ' Role (orden estable) → carpeta anidada (taxonomía de assets de Bethesda) → clip.
            For Each roleGrp In shownNormal.SelectMany(Function(cl) RolesOf(cl).Select(Function(r) (Role:=r, Clip:=cl))).
                                       GroupBy(Function(x) x.Role).OrderBy(Function(g) RoleOrder(g.Key))
                Dim roleNode As New TreeNode(RoleDisplay(roleGrp.Key))
                ' Dedupe de nodos-carpeta por padre (segmento, OrdinalIgnoreCase).
                Dim childIndex As New Dictionary(Of TreeNode, Dictionary(Of String, TreeNode))()
                For Each cl In roleGrp.Select(Function(x) x.Clip).Distinct().OrderBy(Function(cc) ClipDisplayName(cc), StringComparer.OrdinalIgnoreCase)
                    Dim parent As TreeNode = roleNode
                    ' Los clips "sueltos" (cuelgan directo de Animations\, sin carpeta — ej. SneakTurnInPlace, GetUp, Death)
                    ' van bajo un nodo "(general)" para no ensuciar el tope del Role junto a las carpetas weapon-type.
                    Dim folderRel = If(FolderRelOf(cl.AnimationFile) = "", "(general)", FolderRelOf(cl.AnimationFile))
                    If folderRel <> "" Then
                        For Each seg In folderRel.Split("\"c)
                            If seg = "" Then Continue For
                            Dim dict As Dictionary(Of String, TreeNode) = Nothing
                            If Not childIndex.TryGetValue(parent, dict) Then
                                dict = New Dictionary(Of String, TreeNode)(StringComparer.OrdinalIgnoreCase)
                                childIndex(parent) = dict
                            End If
                            Dim folderNode As TreeNode = Nothing
                            If Not dict.TryGetValue(seg, folderNode) Then
                                folderNode = New TreeNode(seg)
                                dict(seg) = folderNode
                                parent.Nodes.Add(folderNode)
                            End If
                            parent = folderNode
                        Next
                    End If
                    Dim leaf As New TreeNode(LeafLabel(cl)) With {.Tag = cl}
                    parent.Nodes.Add(leaf)
                    If toSelect Is Nothing AndAlso _initialClip IsNot Nothing AndAlso cl Is _initialClip Then toSelect = leaf
                Next
                ' Orden alfabético de carpetas (las hojas ya vienen ordenadas por inserción) + sufijo (count) por nodo-carpeta.
                SortAndCountFolders(roleNode)
                roleNode.Text = $"{roleNode.Text} ({CountLeaves(roleNode)})"
                TreeClips.Nodes.Add(roleNode)
            Next

            ' Rama de gestos/diálogo/turns sourced de records IDLE, agrupada por su Category (= evento ENAM del record).
            If shownGesture.Count > 0 Then
                Dim gestureNode As New TreeNode("Gestures & Dialogue (IDLE)")
                For Each catGrp In shownGesture.GroupBy(Function(c) c.Category).OrderByDescending(Function(g) g.Count())
                    Dim catNode As New TreeNode($"{catGrp.Key} ({catGrp.Count()})")
                    For Each cl In catGrp.OrderBy(Function(cc) ClipDisplayName(cc), StringComparer.OrdinalIgnoreCase)
                        Dim leaf As New TreeNode(LeafLabel(cl)) With {.Tag = cl}
                        catNode.Nodes.Add(leaf)
                        If toSelect Is Nothing AndAlso _initialClip IsNot Nothing AndAlso cl Is _initialClip Then toSelect = leaf
                    Next
                    gestureNode.Nodes.Add(catNode)
                Next
                gestureNode.Text = $"{gestureNode.Text} ({shownGesture.Count})"
                TreeClips.Nodes.Add(gestureNode)
            End If

            If toSelect IsNot Nothing Then
                TreeClips.SelectedNode = toSelect
                toSelect.EnsureVisible()
            ElseIf terms.Length > 0 Then
                TreeClips.ExpandAll()
            Else
                For Each n As TreeNode In TreeClips.Nodes : n.Expand() : Next   ' solo el primer nivel
            End If
        Finally
            TreeClips.EndUpdate()
            ' ⛔ Un Invalidate COMPLETO despues del EndUpdate: si no, el control repinta por bandas y las
            ' primeras filas se dibujan antes de que el layout este resuelto.
            TreeClips.Invalidate()
        End Try
        LabelCount.Text = $"{shown.Count} / {_all.Count} clips"
    End Sub

    ' Género: con el checkbox ON, un NPC varón NO ve los clips female-only (RequiresFemale); la mujer ve todo
    ' (juega female + neutral). OFF = todo.
    Private Function PassesGender(c As ResolvedAnimationClip) As Boolean
        If Not CheckFilterGender.Checked Then Return True
        If _isFemale Then Return True
        Return Not c.RequiresFemale
    End Function

    ' 1ª persona: con el checkbox OFF (default) ocultamos los clips alcanzables SOLO vía subgraphs de 1ª persona
    ' (cámara/viewmodel, brazos del player). ON = mostrar todo.
    Private Function PassesPerspective(c As ResolvedAnimationClip) As Boolean
        Return CheckShow1stPerson.Checked OrElse Not c.Is1stPersonOnly
    End Function

    Private Shared Function MatchesAll(c As ResolvedAnimationClip, terms As String()) As Boolean
        If terms Is Nothing OrElse terms.Length = 0 Then Return True
        Dim hay = ClipDisplayName(c) & " " & String.Join(",", c.Roles) & " " & String.Join(",", c.StateAxes) & " " & c.AnimationFile
        For Each t In terms
            If hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        Next
        Return True
    End Function

    Private Shared Function RolesOf(c As ResolvedAnimationClip) As IEnumerable(Of String)
        Return If(c.Roles.Count > 0, c.Roles.AsEnumerable(), {"Other"})
    End Function

    Private Shared Function ClipDisplayName(c As ResolvedAnimationClip) As String
        If Not String.IsNullOrWhiteSpace(c.ClipName) Then Return c.ClipName
        Return System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)
    End Function
''' <summary>Insignias del clip, en un orden FIJO para que la columna quede alineada entre filas.
''' ⊕ aditivo (overlay, no pose standalone) · ° presente en la search-path pero no referenciado
''' estaticamente (evento/dialogo en runtime) · ♀ solo para NPC femenino · ⚠ el crop declarado NO se
''' puede honrar y el clip se reproduce ENTERO.
''' <para>Los PARAMETROS de reproduccion (reversa, rebote, velocidad, crop) NO van aca: van en
''' <see cref="ResolvedAnimationClip.VarianteSufijo"/>, que se calcula UNA vez por lista en el
''' enumerador. Ponerlos aca los recalcularia en cada repintado y ademas darian distinto entre el combo
''' y el picker, que ven listas distintas.</para>
''' <para>⛔ El ⚠ SI va aca y no contradice lo de arriba: no dice "hay crop" (eso lo dice el sufijo)
''' sino "el crop no se pudo honrar", que es otro hecho y de otra fuente. Sale de la pasada LAZY que abre
''' el .hkx — hace falta la Duration del archivo, que el behavior graph no tiene — y lo decide
''' <c>HkxAnimationPlayer.RangoDeCrop</c>, la MISMA funcion que despues aplica el rango. El guard de
''' <c>HkxFlagsKnown</c> evita afirmar que esta todo bien antes de haber leido el archivo.</para></summary>
    Private Shared Function Insignias(c As ResolvedAnimationClip) As String
        Dim b As New Text.StringBuilder(6)
        If c.IsAdditive Then b.Append("⊕ ")
        If Not c.FromBehaviorGraph Then b.Append("° ")
        If c.RequiresFemale Then b.Append("♀ ")
        If c.HkxFlagsKnown AndAlso c.CropIgnorado Then b.Append("⚠ ")
        Return b.ToString()
    End Function

    Private Shared Function LeafLabel(c As ResolvedAnimationClip) As String
        ' Muestra el PATH resuelto en cada hoja para verificar que el clip es del actor correcto (no mis-resuelto).
        ' El frame-count NO se muestra aca porque exigiria cargar+parsear cada .hkx; el del clip activo ya se ve
        ' en el slider de la barra de anim.
        ' ⛔ Este texto es el del NODO: lo usan el ordenamiento y la busqueda incremental del TreeView, asi que
        ' tiene que quedar en texto plano. El dibujo con color lo hace TreeClips_DrawNode a partir del Tag.
        Return $"{Insignias(c)}{ClipDisplayName(c)}{c.VarianteSufijo}   →   {c.AnimationFile}"
    End Function

''' <summary>Dibuja la fila en tres colores: insignias, nombre + variante, y la ruta apagada. Asi el
''' discriminador de variante (el que distingue dos entradas del MISMO archivo) salta a la vista, que es
''' justo el problema que el dedup por variante crea.
''' <para>⛔ Costo: OwnerDrawText solo se dispara para los nodos VISIBLES, no para el arbol entero — con
''' 4.000 clips se dibujan las ~20 filas en pantalla. Los pinceles y la fuente negrita estan cacheados en
''' campos: no se asigna nada por fila. El texto de cada tramo sale de <see cref="LeafParts"/>, que son
''' operaciones de string sobre esas ~20 filas.</para></summary>
    Private Sub TreeClips_DrawNode(sender As Object, e As DrawTreeNodeEventArgs) Handles TreeClips.DrawNode
        ' ⛔ GUARDA OBLIGATORIA: DrawNode se dispara tambien para nodos cuyo layout todavia no existe, y
        ' ahi e.Bounds viene degenerado ({0,0,0,0}). Sin este return, TODOS esos nodos se dibujan en (0,1),
        ' uno encima del otro: es la "mancha" multicolor que quedaba en la primera fila hasta que algo
        ' forzaba un repintado completo. Un nodo REAL y visible siempre tiene alto y ancho positivos.
        If e.Bounds.Height <= 0 OrElse e.Bounds.Width <= 0 Then Return
        If e.Node Is Nothing OrElse e.Node.TreeView Is Nothing Then Return

        ' ⛔ El fondo se pinta SIEMPRE, no solo el de la fila seleccionada: con OwnerDrawText lo que no
        ' pinto yo conserva lo que hubiera en el buffer.
        Dim seleccionado = (e.State And TreeNodeStates.Selected) <> 0
        Dim rFila = New Drawing.Rectangle(e.Bounds.Left, e.Bounds.Top,
                                          Math.Max(e.Bounds.Width, TreeClips.ClientSize.Width - e.Bounds.Left),
                                          e.Bounds.Height)
        e.Graphics.FillRectangle(If(seleccionado, _brSel, _brFondo), rFila)
        ' ⛔ Recorte a la fila: sin esto, un texto largo (las rutas lo son) puede pintar sobre la fila de
        ' al lado cuando el control decide repintar solo una banda.
        ' ⛔ `Graphics.Clip` devuelve una Region NUEVA en CADA lectura —medido: `ReferenceEquals(g.Clip,
        ' g.Clip)` da False y disponer una no toca la otra—, o sea un objeto GDI+ finalizable por cada
        ' DrawNode, que corre por nodo VISIBLE y por repintado (cientos por segundo mientras se tipea en
        ' el buscador). `Save`/`Restore` no reserva nada, restaura el clip EXACTAMENTE igual —medido con
        ' el clip previo infinito Y con el clip de banda de un repintado parcial— y cubre además el
        ' `Return` temprano de la rama de nodo-carpeta, que antes restauraba dos veces.
        Dim estadoGraficos = e.Graphics.Save()
        e.Graphics.SetClip(rFila)
        Try
        Dim clip = TryCast(e.Node.Tag, ResolvedAnimationClip)
        Dim x = e.Bounds.Left
        Dim y = e.Bounds.Top + 1
        Dim fuente = TreeClips.Font

            If clip Is Nothing Then
                ' Nodo de carpeta / rol: el nombre en negrita y el "(N)" apagado.
                Dim txt = e.Node.Text
                Dim iPar = txt.LastIndexOf(" (", StringComparison.Ordinal)
                Dim nombre = If(iPar > 0, txt.Substring(0, iPar), txt)
                Dim cuenta = If(iPar > 0, txt.Substring(iPar), "")
                x = Pintar(e.Graphics, nombre, _fuenteNegrita, If(seleccionado, Drawing.SystemColors.HighlightText, Drawing.SystemColors.WindowText), x, y)
                If cuenta <> "" Then Pintar(e.Graphics, cuenta, fuente, If(seleccionado, Drawing.SystemColors.HighlightText, _brConteo.Color), x, y)
                Return
            End If

            Dim ins = Insignias(clip)
            Dim nom = ClipDisplayName(clip)
            Dim var = clip.VarianteSufijo
            Dim ruta = "   →   " & clip.AnimationFile
            Dim cNombre = If(seleccionado, Drawing.SystemColors.HighlightText, _brNombre.Color)
            Dim cRuta = If(seleccionado, Drawing.SystemColors.HighlightText, _brRuta.Color)
            Dim cVar = If(seleccionado, Drawing.SystemColors.HighlightText, _brVariante.Color)
            Dim cIns = If(seleccionado, Drawing.SystemColors.HighlightText, _brInsignia.Color)

            If ins <> "" Then x = Pintar(e.Graphics, ins, fuente, cIns, x, y)
            x = Pintar(e.Graphics, nom, fuente, cNombre, x, y)
            If var <> "" Then x = Pintar(e.Graphics, var, _fuenteNegrita, cVar, x, y)
            Pintar(e.Graphics, ruta, fuente, cRuta, x, y)
        Finally
            e.Graphics.Restore(estadoGraficos)
        End Try
    End Sub

    ' Dibuja un tramo y devuelve la X donde sigue el proximo. NoPrefix es OBLIGATORIO: las rutas pueden
    ' tener "&" y TextRenderer lo interpretaria como mnemonico y se comeria el caracter siguiente.
    Private Shared Function Pintar(g As Drawing.Graphics, txt As String, f As Drawing.Font,
                                   color As Drawing.Color, x As Integer, y As Integer) As Integer
        Const FLAGS As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine
        TextRenderer.DrawText(g, txt, f, New Drawing.Point(x, y), color, FLAGS)
        Return x + TextRenderer.MeasureText(g, txt, f, Drawing.Size.Empty, FLAGS).Width
    End Function

    ' Carpeta del clip relativa a "Animations\" (la taxonomía de assets de Bethesda: Weapon\Pistol, 1HM, MT\Neutral…).
    ' Normaliza separadores; "" si el clip cuelga directo de Animations\ o si no hay marcador "Animations\".
    Private Shared Function FolderRelOf(animFile As String) As String
        If String.IsNullOrWhiteSpace(animFile) Then Return ""
        Dim norm = animFile.Replace("/"c, "\"c)
        Dim i = norm.IndexOf("Animations\", StringComparison.OrdinalIgnoreCase)
        If i < 0 Then Return ""
        Dim rest = norm.Substring(i + "Animations\".Length)
        Dim j = rest.LastIndexOf("\"c)
        Return If(j > 0, rest.Substring(0, j), "")
    End Function

    ' Ordena recursivamente los nodos-carpeta (alfabético OrdinalIgnoreCase; hoja = tiene Tag, no se reordena ni cuenta
    ' como carpeta) y les anexa el sufijo (count) = hojas descendientes.
    Private Shared Sub SortAndCountFolders(node As TreeNode)
        Dim folders = node.Nodes.Cast(Of TreeNode)().Where(Function(n) n.Tag Is Nothing).
                          OrderBy(Function(n) n.Text, StringComparer.OrdinalIgnoreCase).ToList()
        If folders.Count > 0 Then
            ' Re-inserta las carpetas ordenadas antes de las hojas (las hojas ya vienen ordenadas por inserción).
            For Each f In folders
                node.Nodes.Remove(f)
            Next
            For idx = folders.Count - 1 To 0 Step -1
                node.Nodes.Insert(0, folders(idx))
            Next
            For Each f In folders
                SortAndCountFolders(f)
                f.Text = $"{f.Text} ({CountLeaves(f)})"
            Next
        End If
    End Sub

    ' Hojas descendientes (nodos con Tag = clip) bajo un nodo.
    Private Shared Function CountLeaves(node As TreeNode) As Integer
        Dim total = 0
        For Each n As TreeNode In node.Nodes
            If n.Tag IsNot Nothing Then
                total += 1
            Else
                total += CountLeaves(n)
            End If
        Next
        Return total
    End Function

    ' Role: orden + nombre "humano". El clip trae MT/Weapon/Furniture/Idle/Pipboy/Core/Other.
    Private Shared Function RoleOrder(r As String) As Integer
        Select Case r
            Case "Core" : Return 0
            Case "MT" : Return 1
            Case "Weapon" : Return 2
            Case "Furniture" : Return 3
            Case "Idle" : Return 4
            Case "Pipboy" : Return 5
            Case Else : Return 9
        End Select
    End Function
    Private Shared Function RoleDisplay(r As String) As String
        Select Case r
            Case "Core" : Return "Core (death / get-up / swim)"
            Case "MT" : Return "Locomotion (MT)"
            Case "Weapon" : Return "Weapon / combat"
            Case "Furniture" : Return "Furniture"
            Case "Idle" : Return "Idle"
            Case "Pipboy" : Return "Pipboy"
            Case Else : Return r
        End Select
    End Function
End Class
