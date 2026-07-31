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

''' <summary>String / mesh-key name utilities. Extracted from MainForm (pure stateless, no instance state, no UI). Real separate
''' class (NOT a partial). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NameUtils
    Private Sub New()
    End Sub

    ''' <summary>Quita el sufijo de instancia numérico tras el último "|" (p.ej. "C-X|2" → "C-X").
    ''' Si no hay "|", o no hay dígitos tras el "|", o algún char tras el "|" no es dígito,
    ''' devuelve s sin cambios.</summary>
    Public Shared Function StripInstanceSuffix(s As String) As String
        If String.IsNullOrEmpty(s) Then Return s
        Dim pp = s.LastIndexOf("|"c)
        If pp <= 0 OrElse pp >= s.Length - 1 Then Return s
        For Each c In s.Substring(pp + 1)
            If Not Char.IsDigit(c) Then Return s
        Next
        Return s.Substring(0, pp)
    End Function

    ''' <summary>Quita el sufijo "_faceBones" (case-insensitive) del nombre del shape para hacer
    ''' match con el shape correspondiente en el NIF base. Preserva ":N" (subindex de BSSubIndexTriShape).
    ''' Ej: "BaseFemaleHeadRear_faceBones:0" → "BaseFemaleHeadRear:0".</summary>
    Public Shared Function StripFaceBonesSuffix(name As String) As String
        If String.IsNullOrEmpty(name) Then Return name
        Const Suffix As String = "_faceBones"
        Dim idx = name.IndexOf(Suffix, StringComparison.OrdinalIgnoreCase)
        If idx < 0 Then Return name
        Return String.Concat(name.AsSpan(0, idx), name.AsSpan(idx + Suffix.Length))
    End Function

    ''' <summary>Thin wrapper over <see cref="MeshPathHelpers.NormalizeMeshKey"/>; centralizes
    ''' path normalization in MeshPathHelpers so render path + offline bake never drift.</summary>
    Public Shared Function NormalizeDictionaryKeyWithMeshesPrefix(path As String) As String
        Return MeshPathHelpers.NormalizeMeshKey(path)
    End Function

End Class
