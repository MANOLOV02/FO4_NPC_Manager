Imports System.ComponentModel
Imports System.Drawing.Drawing2D

''' <summary>Compact integer slider: a thin track with a small round handle.
'''
''' Exists because <c>TrackBar</c> is visually oversized for a value that sits inline in a form grid —
''' it reserves room for tick marks and a chunky handle, so a single row grows to ~28 px and it does not
''' line up with the NumericUpDown boxes next to it.
'''
''' Purely visual + arithmetic: no engine or record state. The host wires <see cref="ValueChanged"/>.
'''
''' Sizes are expressed in logical units and passed through <c>LogicalToDeviceUnits</c> so the control
''' keeps its proportions at 125/150/200 % scaling — the app is distributed, so "looks right on my box"
''' is not a criterion.</summary>
<DefaultEvent("ValueChanged")>
Public Class TinySlider
    Inherits Control

    Private _minimum As Integer = 0
    Private _maximum As Integer = 100
    Private _value As Integer = 0
    Private _dragging As Boolean

    Public Event ValueChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or
                 ControlStyles.UserPaint Or
                 ControlStyles.Selectable, True)
        TabStop = True
        Height = 18
    End Sub

    <DefaultValue(0)>
    Public Property Minimum As Integer
        Get
            Return _minimum
        End Get
        Set(v As Integer)
            _minimum = v
            If _maximum < _minimum Then _maximum = _minimum
            Value = _value
            Invalidate()
        End Set
    End Property

    <DefaultValue(100)>
    Public Property Maximum As Integer
        Get
            Return _maximum
        End Get
        Set(v As Integer)
            _maximum = v
            If _maximum < _minimum Then _maximum = _minimum
            Value = _value
            Invalidate()
        End Set
    End Property

    <DefaultValue(0)>
    Public Property Value As Integer
        Get
            Return _value
        End Get
        Set(v As Integer)
            Dim clamped = Math.Max(_minimum, Math.Min(_maximum, v))
            If clamped = _value Then Return
            _value = clamped
            Invalidate()
            RaiseEvent ValueChanged(Me, EventArgs.Empty)
        End Set
    End Property

    ''' <summary>Radio del handle y grosor de la pista, en unidades de dispositivo.</summary>
    Private ReadOnly Property HandleRadius As Integer
        Get
            Return LogicalToDeviceUnits(6)
        End Get
    End Property

    Private ReadOnly Property TrackThickness As Integer
        Get
            Return Math.Max(2, LogicalToDeviceUnits(4))
        End Get
    End Property

    ''' <summary>Recorrido útil: el centro del handle nunca sale de la pista, así que los extremos se
    ''' recortan por su radio. Sin esto el 0 % y el 100 % dibujan el handle cortado por el borde.</summary>
    Private ReadOnly Property TrackLeft As Integer
        Get
            Return HandleRadius
        End Get
    End Property

    Private ReadOnly Property TrackWidth As Integer
        Get
            Return Math.Max(1, Width - HandleRadius * 2)
        End Get
    End Property

    Private Function ValueToX(v As Integer) As Integer
        If _maximum = _minimum Then Return TrackLeft
        Return TrackLeft + CInt(CDbl(v - _minimum) / (_maximum - _minimum) * TrackWidth)
    End Function

    Private Function XToValue(x As Integer) As Integer
        If TrackWidth <= 0 Then Return _minimum
        Dim frac = CDbl(x - TrackLeft) / TrackWidth
        Return _minimum + CInt(Math.Round(frac * (_maximum - _minimum)))
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim midY = Height \ 2
        Dim th = TrackThickness
        Dim trackRect As New Rectangle(TrackLeft, midY - th \ 2, TrackWidth, th)
        Dim handleX = ValueToX(_value)

        Dim baseColor = If(Enabled, SystemColors.ControlDark, SystemColors.ControlLight)
        Dim fillColor = If(Enabled, SystemColors.Highlight, SystemColors.ControlDark)

        Using b As New SolidBrush(baseColor)
            FillRounded(e.Graphics, b, trackRect, th)
        End Using
        If handleX > trackRect.Left Then
            Dim filled As New Rectangle(trackRect.Left, trackRect.Top, handleX - trackRect.Left, th)
            Using b As New SolidBrush(fillColor)
                FillRounded(e.Graphics, b, filled, th)
            End Using
        End If

        Dim r = HandleRadius
        Dim handleRect As New Rectangle(handleX - r, midY - r, r * 2, r * 2)
        Using b As New SolidBrush(If(Enabled, SystemColors.Window, SystemColors.Control))
            e.Graphics.FillEllipse(b, handleRect)
        End Using
        Using p As New Pen(fillColor, If(Focused, 2.0F, 1.5F))
            e.Graphics.DrawEllipse(p, handleRect)
        End Using
    End Sub

    ''' <summary>Rectángulo con las puntas redondeadas al grosor de la pista. Con ancho menor al radio
    ''' el arco no cierra, así que ahí se dibuja recto.</summary>
    Private Shared Sub FillRounded(g As Graphics, b As Brush, r As Rectangle, thickness As Integer)
        If r.Width <= 0 OrElse r.Height <= 0 Then Return
        Dim d = Math.Min(thickness, r.Width)
        If d < 2 Then
            g.FillRectangle(b, r)
            Return
        End If
        Using path As New GraphicsPath()
            path.AddArc(r.Left, r.Top, d, d, 90, 180)
            path.AddArc(r.Right - d, r.Top, d, d, 270, 180)
            path.CloseFigure()
            g.FillPath(b, path)
        End Using
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left Then Return
        Focus()
        _dragging = True
        Value = XToValue(e.X)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _dragging Then Value = XToValue(e.X)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        _dragging = False
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Value += Math.Sign(e.Delta)
    End Sub

    ''' <summary>Las flechas tienen que llegar acá y no navegar entre controles: sin esto el teclado no
    ''' puede mover el slider una vez que tiene el foco.</summary>
    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)
        Select Case e.KeyCode
            Case Keys.Left, Keys.Down : Value -= 1 : e.Handled = True
            Case Keys.Right, Keys.Up : Value += 1 : e.Handled = True
            Case Keys.PageDown : Value -= 10 : e.Handled = True
            Case Keys.PageUp : Value += 10 : e.Handled = True
            Case Keys.Home : Value = _minimum : e.Handled = True
            Case Keys.End : Value = _maximum : e.Handled = True
        End Select
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        MyBase.OnEnabledChanged(e)
        Invalidate()
    End Sub
End Class
