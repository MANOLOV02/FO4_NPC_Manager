Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Meatcap classification of NIF shapes / sub-segments. Extracted from MainForm (pure stateless, no instance state, no UI). Real separate
''' class (NOT a partial). See project_mainform_split.</summary>
Friend NotInheritable Class ShapeMeatcapClassifier
    Private Sub New()
    End Sub

    ''' <summary>Devuelve la clasificación meatcap de un sub-segment. Reglas duras del NIF
    ''' arriba; cualquier otro valor (incluido 0, los rangos biped 30..61 y rangos robot
    ''' 65..95) cae en Normal. Función pura, sin side effects, llamable durante load.</summary>
    Public Shared Function ClassifyMeatcap(sub_ As BSTriShapeGeometry.NifSubSegmentInfo) As MainForm.MeatcapClassification
        If sub_ Is Nothing Then Return MainForm.MeatcapClassification.Normal
        Dim slot As UInteger = sub_.UserSlotID
        ' Confirmed: BSDismemberBodyPartType SECTIONCAP_* y TORSOCAP_* — enum NIF.
        If (slot >= 101UI AndAlso slot <= 113UI) OrElse (slot >= 201UI AndAlso slot <= 213UI) Then
            Return MainForm.MeatcapClassification.Confirmed
        End If
        ' Tentative: BS-OS .xrc los etiqueta "Gore", Bethesda no los confirma. Auditable.
        If slot = 100UI OrElse slot = 102UI OrElse slot = 103UI Then
            Return MainForm.MeatcapClassification.Tentative
        End If
        Return MainForm.MeatcapClassification.Normal
    End Function

    ''' <summary>Clasifica una shape entera mirando todos sus sub-segments. Una shape se
    ''' considera meatcap si CUALQUIER sub no-vacío (numTris>0) lo es. Devuelve la peor
    ''' clasificación encontrada (Confirmed > Tentative > Normal) para que el log distinga
    ''' shapes 100% spec de las dependientes de BS-OS. Shapes sin BSSubIndexTriShape o sin
    ''' segmentation devuelven Normal.</summary>
    Public Shared Function ClassifyShapeMeatcap(geom As IShapeGeometry) As MainForm.MeatcapClassification
        If geom Is Nothing Then Return MainForm.MeatcapClassification.Normal
        Dim subIndex = TryCast(geom.BackingShape, BSSubIndexTriShape)
        If subIndex Is Nothing Then Return MainForm.MeatcapClassification.Normal
        Dim snap = BSTriShapeGeometry.GetSegmentation(subIndex)
        If snap.IsEmpty Then Return MainForm.MeatcapClassification.Normal

        Dim worst As MainForm.MeatcapClassification = MainForm.MeatcapClassification.Normal
        For Each parentSeg In snap.Info.Segs
            If parentSeg.Subs Is Nothing Then Continue For
            For Each sub_ In parentSeg.Subs
                Dim c = ClassifyMeatcap(sub_)
                If c > worst Then worst = c
                If worst = MainForm.MeatcapClassification.Confirmed Then Return worst ' early exit
            Next
        Next
        Return worst
    End Function

End Class
