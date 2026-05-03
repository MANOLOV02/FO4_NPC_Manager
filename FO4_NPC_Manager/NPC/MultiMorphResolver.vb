Imports FO4_Base_Library

' ==========================================================================
' Composes multiple IMorphResolver instances into one. Each delegate's plan is
' merged into the final plan in declaration order; ApplyMorphPlan iterates the
' combined channels normally so deltas accumulate the same way they would if
' channels came from a single resolver.
'
' App-local for now (eje A). Promote to FO4_Base_Library if Wardrobe_Manager
' ever needs to combine resolvers (e.g. to load a LooksMenu preset on top of an
' OSP slider set).
' ==========================================================================

Public Class MultiMorphResolver
    Implements IMorphResolver

    Private ReadOnly _delegates As IMorphResolver()

    ''' <summary>Create a composite from non-null delegates. Null entries are filtered
    ''' so callers can pass conditional resolvers without guard checks.</summary>
    Public Sub New(ParamArray delegates As IMorphResolver())
        _delegates = If(delegates, Array.Empty(Of IMorphResolver)()).Where(Function(r) r IsNot Nothing).ToArray()
    End Sub

    Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan _
            Implements IMorphResolver.ResolveMorphPlan
        Dim merged As New MorphPlan()
        For Each r In _delegates
            Dim sub_plan = r.ResolveMorphPlan(shape, geom)
            If sub_plan Is Nothing OrElse sub_plan.Channels Is Nothing Then Continue For
            merged.Channels.AddRange(sub_plan.Channels)
        Next
        Return merged
    End Function

    ''' <summary>True when no delegates were registered (caller can skip the resolver
    ''' entirely and pass Nothing to the render intent).</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return _delegates.Length = 0
        End Get
    End Property
End Class
