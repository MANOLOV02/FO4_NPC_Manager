Imports System.ComponentModel
Imports System.Drawing.Drawing2D

''' <summary>Barycentric weight picker for the FO4 (Thin, Muscular, Fat) MWGT triple.
'''
''' Engine semantics: the three weights are barycentric coordinates that sum to 1.0 and are all
''' ≥ 0. The Creation Kit shows them as a draggable point inside a triangle whose vertices are
''' the three pure archetypes. We replicate that UI: the user drags a point inside the triangle,
''' the (T, M, F) tuple is read out as barycentric coordinates of the cursor position, and the
''' WeightChanged event fires with the normalized triple.
'''
''' The control draws the triangle, the cursor dot, and three vertex labels. Pure visual + math —
''' no record / engine state. Host wires WeightChanged to slider sync and preview repaint.</summary>
<DefaultEvent("WeightChanged")>
Public Class WeightTriangleControl
    Inherits Control

    Private _thin As Single = 0.0F
    Private _muscular As Single = 0.0F
    Private _fat As Single = 0.0F
    Private _dragging As Boolean = False

    Public Event WeightChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or
                 ControlStyles.UserPaint, True)
        DoubleBuffered = True
        BackColor = SystemColors.Control
        MinimumSize = New Size(160, 140)
    End Sub

    ''' <summary>Set the triple atomically. Values are clamped to [0,1] and renormalized to sum=1
    ''' (degenerate input — all zeros — falls back to (0.5, 0.5, 0)). Suppresses WeightChanged so
    ''' the host can call this during slider syncs without re-entering its own handler.</summary>
    Public Sub SetWeights(t As Single, m As Single, f As Single, Optional fireChangeEvent As Boolean = False)
        Dim n = Normalize(t, m, f)
        _thin = n.t
        _muscular = n.m
        _fat = n.f
        Invalidate()
        If fireChangeEvent Then RaiseEvent WeightChanged(Me, EventArgs.Empty)
    End Sub

    <Browsable(False)>
    Public ReadOnly Property Thin As Single
        Get
            Return _thin
        End Get
    End Property

    <Browsable(False)>
    Public ReadOnly Property Muscular As Single
        Get
            Return _muscular
        End Get
    End Property

    <Browsable(False)>
    Public ReadOnly Property Fat As Single
        Get
            Return _fat
        End Get
    End Property

    Private Shared Function Normalize(t As Single, m As Single, f As Single) As (t As Single, m As Single, f As Single)
        Dim tt = Math.Max(0.0F, t)
        Dim mm = Math.Max(0.0F, m)
        Dim ff = Math.Max(0.0F, f)
        Dim s = tt + mm + ff
        If s < 0.0001F Then Return (0.5F, 0.5F, 0.0F)
        Return (tt / s, mm / s, ff / s)
    End Function

    ''' <summary>Vertex coords in client-pixel space, recomputed each paint to handle resize.
    ''' Layout: Muscular top-center, Thin bottom-left, Fat bottom-right. The triangle is
    ''' equilateral (height = side·√3/2) and centered inside the available space — we don't
    ''' stretch it to fill a non-square client rect.</summary>
    Private Function GetTriangleVertices() As (vThin As PointF, vMusc As PointF, vFat As PointF)
        Const margin As Integer = 18
        Dim availW = Math.Max(1, ClientSize.Width - 2 * margin)
        Dim availH = Math.Max(1, ClientSize.Height - 2 * margin)
        ' Equilateral: height/side = √3/2. Pick the largest "side" that fits both dimensions.
        Const triH As Single = 0.86602540378F  ' √3/2
        Dim sideFromW As Single = availW
        Dim sideFromH As Single = availH / triH
        Dim side As Single = Math.Min(sideFromW, sideFromH)
        Dim height As Single = side * triH
        ' Center the bounding box of the triangle inside the available rect.
        Dim leftPad As Single = margin + (availW - side) / 2.0F
        Dim topPad As Single = margin + (availH - height) / 2.0F
        Dim vMusc = New PointF(leftPad + side / 2.0F, topPad)
        Dim vThin = New PointF(leftPad, topPad + height)
        Dim vFat = New PointF(leftPad + side, topPad + height)
        Return (vThin, vMusc, vFat)
    End Function

    ''' <summary>Convert (t, m, f) barycentric → pixel point.</summary>
    Private Function BaryToPixel(t As Single, m As Single, f As Single) As PointF
        Dim v = GetTriangleVertices()
        Return New PointF(
            v.vThin.X * t + v.vMusc.X * m + v.vFat.X * f,
            v.vThin.Y * t + v.vMusc.Y * m + v.vFat.Y * f)
    End Function

    ''' <summary>Convert pixel point → (t, m, f) barycentric, clamped inside the triangle.
    ''' Algorithm: signed-area solve, then if any coordinate is negative project the point to the
    ''' closest point on the triangle edge.</summary>
    Private Function PixelToBary(p As PointF) As (t As Single, m As Single, f As Single)
        Dim v = GetTriangleVertices()
        Dim a = v.vThin
        Dim b = v.vMusc
        Dim c = v.vFat

        ' Standard barycentric solve.
        Dim denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y)
        If Math.Abs(denom) < 0.0001F Then Return (1.0F, 0.0F, 0.0F)
        Dim t = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / denom
        Dim m = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / denom
        Dim f = 1.0F - t - m

        ' Clamp outside-triangle clicks by projecting to the closest edge.
        If t < 0 OrElse m < 0 OrElse f < 0 Then
            Dim proj = ProjectOntoTriangle(p, a, b, c)
            t = ((b.Y - c.Y) * (proj.X - c.X) + (c.X - b.X) * (proj.Y - c.Y)) / denom
            m = ((c.Y - a.Y) * (proj.X - c.X) + (a.X - c.X) * (proj.Y - c.Y)) / denom
            f = 1.0F - t - m
            t = Math.Max(0.0F, t) : m = Math.Max(0.0F, m) : f = Math.Max(0.0F, f)
            Dim s = t + m + f
            If s > 0.0001F Then t /= s : m /= s : f /= s
        End If
        Return (CSng(t), CSng(m), CSng(f))
    End Function

    Private Shared Function ProjectOntoTriangle(p As PointF, a As PointF, b As PointF, c As PointF) As PointF
        ' Project to each edge, return the closest projection.
        Dim ab = ProjectOntoSegment(p, a, b)
        Dim bc = ProjectOntoSegment(p, b, c)
        Dim ca = ProjectOntoSegment(p, c, a)
        Dim dab = DistSq(p, ab)
        Dim dbc = DistSq(p, bc)
        Dim dca = DistSq(p, ca)
        Dim best = ab
        Dim bestD = dab
        If dbc < bestD Then best = bc : bestD = dbc
        If dca < bestD Then best = ca
        Return best
    End Function

    Private Shared Function ProjectOntoSegment(p As PointF, a As PointF, b As PointF) As PointF
        Dim abx = b.X - a.X
        Dim aby = b.Y - a.Y
        Dim len2 = abx * abx + aby * aby
        If len2 < 0.0001F Then Return a
        Dim t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2
        t = CSng(Math.Max(0.0F, Math.Min(1.0F, t)))
        Return New PointF(a.X + abx * t, a.Y + aby * t)
    End Function

    Private Shared Function DistSq(a As PointF, b As PointF) As Single
        Dim dx = a.X - b.X
        Dim dy = a.Y - b.Y
        Return dx * dx + dy * dy
    End Function

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = MouseButtons.Left Then
            _dragging = True
            UpdateFromMouse(e.Location)
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _dragging Then UpdateFromMouse(e.Location)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If e.Button = MouseButtons.Left Then _dragging = False
    End Sub

    Private Sub UpdateFromMouse(p As Point)
        Dim b = PixelToBary(New PointF(p.X, p.Y))
        _thin = b.t
        _muscular = b.m
        _fat = b.f
        ' Force a synchronous repaint of THIS control before raising the event. The host's
        ' WeightChanged handler kicks off a full preview render which is heavy and would block
        ' the message pump until done — without Update() the dot wouldn't move on screen until
        ' after the render finishes, making the UI feel laggy. Update() drains the WM_PAINT
        ' that Invalidate() queued for this control only, before we hand control to the host.
        Invalidate()
        Update()
        RaiseEvent WeightChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim v = GetTriangleVertices()

        ' Triangle fill + outline.
        Using fill As New SolidBrush(Color.FromArgb(232, 240, 250))
            g.FillPolygon(fill, New PointF() {v.vThin, v.vMusc, v.vFat})
        End Using
        Using pen As New Pen(Color.FromArgb(80, 110, 150), 1.5F)
            g.DrawPolygon(pen, New PointF() {v.vThin, v.vMusc, v.vFat})
        End Using

        ' Vertex labels — Muscular top, Thin bottom-left, Fat bottom-right.
        Using fnt As New Font(Font.FontFamily, 8.0F, FontStyle.Bold)
            Using br As New SolidBrush(ForeColor)
                Dim fmtTop As New StringFormat() With {
                    .Alignment = StringAlignment.Center,
                    .LineAlignment = StringAlignment.Far
                }
                g.DrawString("Muscular", fnt, br, v.vMusc.X, v.vMusc.Y - 2, fmtTop)
                Dim fmtBottomLeft As New StringFormat() With {
                    .Alignment = StringAlignment.Far,
                    .LineAlignment = StringAlignment.Near
                }
                g.DrawString("Thin", fnt, br, v.vThin.X - 2, v.vThin.Y + 2, fmtBottomLeft)
                Dim fmtBottomRight As New StringFormat() With {
                    .Alignment = StringAlignment.Near,
                    .LineAlignment = StringAlignment.Near
                }
                g.DrawString("Fat", fnt, br, v.vFat.X + 2, v.vFat.Y + 2, fmtBottomRight)
            End Using
        End Using

        ' Cursor dot at current barycentric coordinates.
        Dim p = BaryToPixel(_thin, _muscular, _fat)
        Const r As Integer = 5
        Using br As New SolidBrush(Color.FromArgb(220, 60, 60))
            g.FillEllipse(br, p.X - r, p.Y - r, 2 * r, 2 * r)
        End Using
        Using pen As New Pen(Color.White, 1.5F)
            g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r)
        End Using
    End Sub
End Class
