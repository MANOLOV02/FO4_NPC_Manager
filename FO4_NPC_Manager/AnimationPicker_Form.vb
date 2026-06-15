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
    Private ReadOnly _initialFile As String
    Private ReadOnly _isFemale As Boolean

    ''' <summary>Clip elegido (Nothing si se canceló o si el nodo seleccionado es una categoría).</summary>
    Public ReadOnly Property SelectedClip As ResolvedAnimationClip
        Get
            Return TryCast(TreeClips.SelectedNode?.Tag, ResolvedAnimationClip)
        End Get
    End Property

    Public Sub New(clips As IEnumerable(Of ResolvedAnimationClip), isFemale As Boolean, Optional currentFile As String = Nothing)
        InitializeComponent()
        _all = If(clips, Enumerable.Empty(Of ResolvedAnimationClip)()).Where(Function(c) c IsNot Nothing).ToList()
        _isFemale = isFemale
        _initialFile = If(currentFile, "")
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
                    If toSelect Is Nothing AndAlso _initialFile <> "" AndAlso String.Equals(cl.AnimationFile, _initialFile, StringComparison.OrdinalIgnoreCase) Then toSelect = leaf
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
                        If toSelect Is Nothing AndAlso _initialFile <> "" AndAlso String.Equals(cl.AnimationFile, _initialFile, StringComparison.OrdinalIgnoreCase) Then toSelect = leaf
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
    Private Shared Function LeafLabel(c As ResolvedAnimationClip) As String
        Dim g = If(c.RequiresFemale, " ♀", "")
        Dim add = If(c.IsAdditive, "⊕ ", "")   ' insignia aditivo (overlay, no pose standalone)
        Dim sp = If(c.FromBehaviorGraph, "", "° ")   ' °=presente en la search-path pero no referenciado estáticamente (evento/diálogo en runtime)
        ' Muestra el PATH resuelto en cada hoja para verificar que el clip es del actor correcto (no mis-resuelto).
        ' (Sin marca de PlaybackSpeed: era 1.0F en casi todos los clips ⇒ "[1x]" ruido. El frame-count NO se muestra
        '  acá porque exigiría cargar+parsear cada .hkx; el del clip activo ya se ve en el slider de la barra de anim.)
        Return $"{sp}{add}{ClipDisplayName(c)}{g}   →   {c.AnimationFile}"
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
