Option Strict On
Imports System.IO
Imports System.Text.Json

''' <summary>Per-plugin JSON sidecar storing the FOMOD-export metadata the user edits in
''' <see cref="FomodExport_Form"/> (mod name, author, version, description, website, extra assets).
''' One file per plugin: <c>&lt;plugin&gt;.fomodmeta.json</c> next to <c>&lt;plugin&gt;.esp</c> —
''' same placement idiom as <see cref="BssliderSidecar"/>, so everything the app persists for a
''' plugin travels with the plugin file.
'''
''' NEVER included in the exported ZIP: it is authoring state, not mod payload — the same data
''' ships to the end user as <c>fomod\info.xml</c> (see <see cref="FomodExporter.BuildInfoXml"/>).</summary>
Public Module FomodMetaSidecar

    Public Const Extension As String = ".fomodmeta.json"
    ''' <summary>v2 added <c>includeScreenshot</c> (persisted checkbox preference). Additive —
    ''' older files load fine (property keeps its default).</summary>
    Public Const SchemaVersion As Integer = 2

    Public Class MetaFile
        Public Property Version As Integer = SchemaVersion
        ''' <summary>Plugin file name (with extension) this metadata belongs to. Informational —
        ''' the binding is the file's location next to the plugin, like the .bssliders sidecar.</summary>
        Public Property Plugin As String = ""
        Public Property ModName As String = ""
        Public Property Author As String = ""
        ''' <summary>Mod version shown in fomod\info.xml. Named ModVersion so it cannot collide
        ''' with the sidecar's own schema <see cref="Version"/> field.</summary>
        Public Property ModVersion As String = "1.0.0"
        Public Property Description As String = ""
        Public Property Website As String = ""
        ''' <summary>Extra asset files the author added to the package, as Data-relative paths
        ''' ("\" separators). Validated against disk at export time.</summary>
        Public Property ExtraAssets As New List(Of String)
        ''' <summary>"Include screenshot" checkbox preference. The screenshot itself is NOT
        ''' persisted — it's captured fresh from the main preview each time the dialog opens.</summary>
        Public Property IncludeScreenshot As Boolean = True
    End Class

    ''' <summary>Build the sidecar path for an ESP/ESM/ESL path: same directory, same basename,
    ''' <see cref="Extension"/> in place of the plugin extension (mirror of BssliderSidecar.BuildPath).</summary>
    Public Function BuildPath(espPath As String) As String
        If String.IsNullOrEmpty(espPath) Then Return ""
        Return Path.ChangeExtension(espPath, Extension)
    End Function

    Private ReadOnly SerializerOptions As New JsonSerializerOptions With {
        .WriteIndented = True,
        .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        .PropertyNameCaseInsensitive = True}

    ''' <summary>Read the metadata sidecar from disk. Returns Nothing when the file is missing,
    ''' unreadable, or not valid JSON — the caller (the export dialog) falls back to defaults.
    ''' Unknown fields are silently ignored (forward-compat, same policy as BssliderSidecar).</summary>
    Public Function Read(path As String) As MetaFile
        If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then Return Nothing
        Try
            Dim raw = File.ReadAllText(path)
            Dim meta = JsonSerializer.Deserialize(Of MetaFile)(raw, SerializerOptions)
            If meta IsNot Nothing AndAlso meta.ExtraAssets Is Nothing Then meta.ExtraAssets = New List(Of String)
            Return meta
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Write the metadata sidecar to disk atomically (.tmp + rename), indented —
    ''' same idiom as <see cref="BssliderSidecar.Write"/> so a crash mid-write never leaves a
    ''' truncated sidecar behind.</summary>
    Public Sub Write(path As String, meta As MetaFile)
        If String.IsNullOrEmpty(path) OrElse meta Is Nothing Then Return
        meta.Version = SchemaVersion
        Dim json = JsonSerializer.Serialize(meta, SerializerOptions)
        Dim tmp = path & ".tmp"
        File.WriteAllText(tmp, json, New Text.UTF8Encoding(False))
        If File.Exists(path) Then File.Delete(path)
        File.Move(tmp, path)
    End Sub

End Module
