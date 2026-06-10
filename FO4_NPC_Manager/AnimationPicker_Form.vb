Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Picker de animaciones en ÁRBOL con categorías "humanas": Role (Locomoción/Arma/Furniture/Idle/
''' Pipboy/Core) → eje de Estado (Normal/Injured/Archetype/Flavor/…) → clip. Filtro de texto + checkbox
''' "Filter by gender" (default ON: si el NPC es varón oculta los clips female-only; mujer ve todo). Lo abre el
''' botón "Select Animation" de la barra de animación de MainForm. Al aceptar, MainForm setea el combo.</summary>
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

    ' Reconstruye el árbol: filtro texto (AND de términos vs nombre+role+file) + filtro de género; agrupa por
    ' Role → eje de Estado → clip. Un clip con varios roles/ejes aparece bajo cada categoría que le corresponde.
    Private Sub Rebuild()
        ' Guard: el Designer setea CheckFilterGender.Checked=True dentro de InitializeComponent() → dispara
        ' CheckedChanged → Rebuild() ANTES de que el ctor asigne _all. El build real corre en Shown.
        If _all Is Nothing Then Return
        Dim terms = TextFilter.Text.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim shown = _all.Where(Function(c) PassesGender(c) AndAlso MatchesAll(c, terms)).ToList()

        TreeClips.BeginUpdate()
        Try
            TreeClips.Nodes.Clear()
            Dim toSelect As TreeNode = Nothing
            ' Role (orden estable) → State axis → clip.
            For Each roleGrp In shown.SelectMany(Function(cl) RolesOf(cl).Select(Function(r) (Role:=r, Clip:=cl))).
                                       GroupBy(Function(x) x.Role).OrderBy(Function(g) RoleOrder(g.Key))
                Dim roleNode As New TreeNode(RoleDisplay(roleGrp.Key))
                For Each axisGrp In roleGrp.SelectMany(Function(x) AxesOf(x.Clip).Select(Function(a) (Axis:=a, Clip:=x.Clip))).
                                            GroupBy(Function(x) x.Axis).OrderBy(Function(g) AxisOrder(g.Key))
                    Dim axisNode As New TreeNode(AxisDisplay(axisGrp.Key))
                    For Each cl In axisGrp.Select(Function(x) x.Clip).Distinct().OrderBy(Function(cc) ClipDisplayName(cc), StringComparer.OrdinalIgnoreCase)
                        Dim leaf As New TreeNode(LeafLabel(cl)) With {.Tag = cl}
                        axisNode.Nodes.Add(leaf)
                        If toSelect Is Nothing AndAlso _initialFile <> "" AndAlso String.Equals(cl.AnimationFile, _initialFile, StringComparison.OrdinalIgnoreCase) Then toSelect = leaf
                    Next
                    axisNode.Text = $"{axisNode.Text} ({axisNode.Nodes.Count})"
                    roleNode.Nodes.Add(axisNode)
                Next
                roleNode.Text = $"{roleNode.Text} ({roleNode.Nodes.Cast(Of TreeNode)().Sum(Function(n) n.Nodes.Count)})"
                TreeClips.Nodes.Add(roleNode)
            Next

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
    Private Shared Function AxesOf(c As ResolvedAnimationClip) As IEnumerable(Of String)
        Return If(c.StateAxes.Count > 0, c.StateAxes.AsEnumerable(), {"Normal"})
    End Function

    Private Shared Function ClipDisplayName(c As ResolvedAnimationClip) As String
        If Not String.IsNullOrWhiteSpace(c.ClipName) Then Return c.ClipName
        Return System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)
    End Function
    Private Shared Function LeafLabel(c As ResolvedAnimationClip) As String
        Dim g = If(c.RequiresFemale, " ♀", "")
        ' Muestra el PATH resuelto en cada hoja para verificar que el clip es del actor correcto (no mis-resuelto).
        Return $"{ClipDisplayName(c)}  [{c.PlaybackSpeed:0.##}x]{g}   →   {c.AnimationFile}"
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

    ' Eje de estado: "Normal" primero, luego el resto alfabético. Nombre sin el prefijo "Anim ".
    Private Shared Function AxisOrder(a As String) As Integer
        Return If(a = "Normal", 0, 1)
    End Function
    Private Shared Function AxisDisplay(a As String) As String
        If a = "Normal" Then Return "Normal"
        Return a.Replace("Anim ", "")   ' "Anim Injured" → "Injured", "Anim Archetype" → "Archetype"
    End Function
End Class
