Imports System.Numerics
Imports FO4_Base_Library
Imports NiflySharp.Blocks

''' <summary>
''' Lee BSConnectPoint::Parents y BSConnectPoint::Children de un NIF.
'''
''' BSConnectPoint::Parents (typically en root NiNode del skeleton del actor):
'''   Cada entry es un "socket" donde se mountea algo:
'''     Name           — string que matchea OMOD.AttachPoint KYWD EditorID
'''     Parent         — bone name al que pertenece el socket (e.g. "Chest_skin")
'''     Rotation/Translation/Scale — transform local respecto al bone padre
'''
''' BSConnectPoint::Children (típicamente en root del NIF de un chunk):
'''   Lista de point names que ESTE chunk espera encontrar como Parent socket.
'''   Sirve como "etiqueta" del lado del chunk para confirmar el match.
'''
''' Schema NIF: nif.xml:8360-8379 (struct BSConnectPoint + ::Parents + ::Children).
''' Engine vanilla usa este mecanismo para Power Armor frame attachments, robot chunks,
''' weapon mods, settlement workshop, etc.
''' </summary>
Public Module BSConnectPointReader

    Public Class ConnectPointInfo
        ''' <summary>Socket name. Match contra OMOD.AttachPoint KYWD EditorID (case-insens).</summary>
        Public Name As String
        ''' <summary>Bone name al que pertenece este socket. El chunk se mountea en transform
        ''' local relativo a este bone del skeleton.</summary>
        Public ParentBoneName As String
        Public Rotation As Quaternion
        Public Translation As Vector3
        Public Scale As Single
    End Class

    ''' <summary>Itera el root NiNode del NIF buscando bloques BSConnectPoint::Parents en su
    ''' ExtraDataList; aplana cada Parent.ConnectPoints en una lista de ConnectPointInfo.</summary>
    Public Function ReadParents(nif As Nifcontent_Class_Manolo) As List(Of ConnectPointInfo)
        Dim result As New List(Of ConnectPointInfo)
        If nif Is Nothing Then Return result

        Dim root = nif.GetRootNode()
        If root Is Nothing OrElse root.ExtraDataList Is Nothing Then Return result

        For Each ref In root.ExtraDataList.References
            Dim block = nif.Blocks(ref.Index)
            Dim parents = TryCast(block, BSConnectPoint_Parents)
            If parents Is Nothing OrElse parents.ConnectPoints Is Nothing Then Continue For
            For Each cp In parents.ConnectPoints
                Dim info As New ConnectPointInfo With {
                    .Name = If(cp.Name?.Content, ""),
                    .ParentBoneName = If(cp.Parent?.Content, ""),
                    .Rotation = cp.Rotation,
                    .Translation = cp.Translation,
                    .Scale = cp.Scale
                }
                result.Add(info)
            Next
        Next

        Return result
    End Function

    ''' <summary>Lee los Children point names del root del NIF (chunk que declara a qué
    ''' sockets espera adjuntarse). Devuelve set vacío si el NIF no tiene Children block.</summary>
    Public Function ReadChildrenNames(nif As Nifcontent_Class_Manolo) As HashSet(Of String)
        Dim result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If nif Is Nothing Then Return result

        Dim root = nif.GetRootNode()
        If root Is Nothing OrElse root.ExtraDataList Is Nothing Then Return result

        For Each ref In root.ExtraDataList.References
            Dim block = nif.Blocks(ref.Index)
            Dim children = TryCast(block, BSConnectPoint_Children)
            If children Is Nothing OrElse children.PointName Is Nothing Then Continue For
            For Each pn In children.PointName
                Dim s = If(pn?.Content, "")
                If s <> "" Then result.Add(s)
            Next
        Next

        Return result
    End Function

End Module
