Imports System.IO
Imports System.Text.Json
Imports System.Globalization
Imports FO4_Base_Library

''' <summary>Load/Save for Fallout 4 <c>.sclp</c> armor-sculpt (per-bone scale) files, plus the
''' bridge between the on-disk ABSOLUTE scale values and the app's ARMA bone-scale model
''' (<see cref="ARMA_BoneScaleGender"/> / <see cref="ARMA_BoneScaleDelta"/>, which store BSMS
''' DELTAS = absolute − 1.0).
'''
''' <para>Format: a JSON array of bone entries. Vanilla Bethesda files nest the axes under a
''' <c>"Scale"</c> object — <c>{ "Name": "&lt;boneName&gt;", "Scale": { "x":, "y":, "z": } }</c>
''' (confirmed by dumping the shipped <c>Meshes\...\*.sclp</c> from the BA2) — while some third-party
''' exporters put x/y/z FLAT on the entry. <see cref="Load"/> accepts BOTH; <see cref="Save"/> writes
''' the nested (vanilla) form. x/y/z are ABSOLUTE scale values (1.0 = unchanged). One <c>.sclp</c> file
''' represents ONE gender (the xEdit importer prompts Male/Female on import).</para>
'''
''' <para>Conversion to BSMS (xEdit-faithful, see the .pas lines 56 + 82-84):
''' the importer SKIPS entries equal to (1.0, 1.0, 1.0), and writes
''' <c>BSMS_delta = absolute − 1.0</c> per axis (X→Value#0, Y→#1, Z→#2). We mirror both rules
''' in <see cref="ToGenderBlock"/>.</para>
'''
''' App-local on purpose: the ARMA editor is the single consumer for now, so this is NOT in the
''' shared library. All float parse/format uses <see cref="CultureInfo.InvariantCulture"/> because
''' the file uses '.' decimal separators regardless of the user's locale.</summary>
Public Module SclpFile

    Public Const Extension As String = ".sclp"

    ''' <summary>One bone entry exactly as it appears in the <c>.sclp</c> file: ABSOLUTE scale
    ''' values (1.0 = unchanged), NOT BSMS deltas. Use <see cref="ToGenderBlock"/> /
    ''' <see cref="FromGenderBlock"/> to convert to/from the ARMA delta model.</summary>
    Public Class SclpBoneAbsolute
        Public Name As String = ""
        Public X As Single = 1.0F
        Public Y As Single = 1.0F
        Public Z As Single = 1.0F
    End Class

    ' =====================================================================
    ' Load
    ' =====================================================================

    ''' <summary>Parse a <c>.sclp</c> file into ABSOLUTE bone entries.
    ''' Lenient about the document shape: accepts an array at the top level, OR an object whose
    ''' single array-valued property is the bone list (some exporters wrap the array). Unknown
    ''' keys on each entry are ignored. Entries are returned verbatim (NOT filtered) — the caller
    ''' decides what to do with identity (1,1,1) rows.</summary>
    ''' <exception cref="IOException">File missing/unreadable.</exception>
    ''' <exception cref="JsonException">Malformed JSON, or no bone array could be located.</exception>
    Public Function Load(path As String) As List(Of SclpBoneAbsolute)
        If String.IsNullOrEmpty(path) Then Throw New ArgumentException("Empty .sclp path.", NameOf(path))
        Dim raw = File.ReadAllText(path)

        Dim opts As New JsonDocumentOptions With {
            .CommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True
        }

        Using doc = JsonDocument.Parse(raw, opts)
            Dim arr As JsonElement = LocateBoneArray(doc.RootElement)
            If arr.ValueKind <> JsonValueKind.Array Then
                Throw New JsonException("No bone array found in .sclp (expected a top-level array, or an object wrapping a single array).")
            End If

            Dim result As New List(Of SclpBoneAbsolute)
            For Each el In arr.EnumerateArray()
                If el.ValueKind <> JsonValueKind.Object Then Continue For
                Dim bone As New SclpBoneAbsolute()

                Dim nameEl As JsonElement
                If el.TryGetProperty("Name", nameEl) AndAlso nameEl.ValueKind = JsonValueKind.String Then
                    bone.Name = nameEl.GetString()
                ElseIf el.TryGetProperty("name", nameEl) AndAlso nameEl.ValueKind = JsonValueKind.String Then
                    bone.Name = nameEl.GetString()
                End If

                ' Axis source: vanilla Bethesda .sclp nests the three axes under a "Scale" object
                ' ({ "Name":, "Scale": { "x":, "y":, "z": } }); some third-party exporters put x/y/z
                ' FLAT on the entry. Read from the Scale sub-object when it's present and an object,
                ' otherwise from the entry itself. (Reading flat from a nested file silently yielded
                ' all-identity — the reason a vanilla .sclp loaded as (1,1,1) everywhere.)
                Dim axisObj As JsonElement = el
                Dim scaleEl As JsonElement
                If (el.TryGetProperty("Scale", scaleEl) OrElse el.TryGetProperty("scale", scaleEl)) _
                   AndAlso scaleEl.ValueKind = JsonValueKind.Object Then
                    axisObj = scaleEl
                End If
                bone.X = ReadAxis(axisObj, "x", "X")
                bone.Y = ReadAxis(axisObj, "y", "Y")
                bone.Z = ReadAxis(axisObj, "z", "Z")
                result.Add(bone)
            Next
            Return result
        End Using
    End Function

    ''' <summary>Resolve the bone array from the document root: the root itself if it's an array,
    ''' otherwise (root is an object) the single array-valued property if there's exactly one.
    ''' Returns the root element unchanged when neither applies (caller validates ValueKind).</summary>
    Private Function LocateBoneArray(root As JsonElement) As JsonElement
        If root.ValueKind = JsonValueKind.Array Then Return root
        If root.ValueKind = JsonValueKind.Object Then
            Dim found As JsonElement = Nothing
            Dim count As Integer = 0
            For Each prop In root.EnumerateObject()
                If prop.Value.ValueKind = JsonValueKind.Array Then
                    found = prop.Value
                    count += 1
                End If
            Next
            If count = 1 Then Return found
        End If
        Return root
    End Function

    ''' <summary>Read one axis value, accepting either the lower-case key (as the xEdit format
    ''' uses) or an upper-case alias. Numbers are read directly; numeric strings are parsed with
    ''' invariant culture (some exporters quote the floats). Missing/non-numeric → 1.0
    ''' (identity, the neutral scale). NaN/Infinity are coerced to 1.0 as a guard.</summary>
    Private Function ReadAxis(obj As JsonElement, lowerKey As String, upperKey As String) As Single
        Dim el As JsonElement
        If Not obj.TryGetProperty(lowerKey, el) Then
            If Not obj.TryGetProperty(upperKey, el) Then Return 1.0F
        End If
        Dim v As Single
        Select Case el.ValueKind
            Case JsonValueKind.Number
                v = el.GetSingle()
            Case JsonValueKind.String
                If Not Single.TryParse(el.GetString(), NumberStyles.Float Or NumberStyles.AllowThousands,
                                       CultureInfo.InvariantCulture, v) Then Return 1.0F
            Case Else
                Return 1.0F
        End Select
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return 1.0F
        Return v
    End Function

    ' =====================================================================
    ' Save
    ' =====================================================================

    ''' <summary>Write the bone list as a pretty-printed JSON array
    ''' <c>[{"Name":,"x":,"y":,"z":}]</c>. Does NOT skip identity (1,1,1) entries — the editor
    ''' decides what to persist; we serialize exactly what we're given. Floats are emitted with
    ''' invariant culture (via System.Text.Json's shortest-roundtrippable formatter). NaN/Infinity
    ''' are guarded to 1.0 so the output is always valid JSON.</summary>
    Public Sub Save(path As String, bones As IEnumerable(Of SclpBoneAbsolute))
        If String.IsNullOrEmpty(path) Then Throw New ArgumentException("Empty .sclp path.", NameOf(path))
        If bones Is Nothing Then bones = Array.Empty(Of SclpBoneAbsolute)()

        Dim opts As New JsonWriterOptions With {.Indented = True}
        Dim bytes() As Byte
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms, opts)
                w.WriteStartArray()
                For Each b In bones
                    If b Is Nothing Then Continue For
                    ' Vanilla Bethesda format: axes nested under a "Scale" object. Load() accepts both
                    ' this and the flat layout, but we WRITE the nested form so the output matches the
                    ' game's own .sclp files (and whatever importer expects them).
                    w.WriteStartObject()
                    w.WriteString("Name", If(b.Name, ""))
                    w.WriteStartObject("Scale")
                    w.WriteNumber("x", SafeAxis(b.X))
                    w.WriteNumber("y", SafeAxis(b.Y))
                    w.WriteNumber("z", SafeAxis(b.Z))
                    w.WriteEndObject()
                    w.WriteEndObject()
                Next
                w.WriteEndArray()
                w.Flush()
            End Using
            bytes = ms.ToArray()
        End Using

        ' Atomic write (.tmp + rename), same idiom as BssliderSidecar.Write.
        Dim tmp = path & ".tmp"
        File.WriteAllBytes(tmp, bytes)
        If File.Exists(path) Then File.Delete(path)
        File.Move(tmp, path)
    End Sub

    Private Function SafeAxis(v As Single) As Single
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then Return 1.0F
        Return v
    End Function

    ' =====================================================================
    ' BSMS <-> sclp conversion bridge
    ' =====================================================================

    ''' <summary>Convert ABSOLUTE sclp values to an <see cref="ARMA_BoneScaleGender"/> with
    ''' DeltaX/Y/Z = (absolute − 1.0). Mirrors the xEdit importer: entries equal to (1,1,1) are
    ''' SKIPPED (they're no-ops in the BSMS = 0 delta sense). Entries with a blank Name are also
    ''' skipped (no bone to attach to).</summary>
    ''' <param name="gender">0 = Male, 1 = Female (xEdit wbSexEnum / ARMA BSMP).</param>
    Public Function ToGenderBlock(bones As IEnumerable(Of SclpBoneAbsolute), gender As UInteger) As ARMA_BoneScaleGender
        Dim block As New ARMA_BoneScaleGender With {.Gender = gender}
        If bones Is Nothing Then Return block
        For Each b In bones
            If b Is Nothing OrElse String.IsNullOrEmpty(b.Name) Then Continue For
            ' xEdit-faithful identity skip (see .pas line 56). Exact 1.0 compare matches the
            ' script's intent: only literal unchanged rows are dropped.
            If b.X = 1.0F AndAlso b.Y = 1.0F AndAlso b.Z = 1.0F Then Continue For
            block.Bones.Add(New ARMA_BoneScaleDelta With {
                .BoneName = b.Name,
                .DeltaX = b.X - 1.0F,
                .DeltaY = b.Y - 1.0F,
                .DeltaZ = b.Z - 1.0F
            })
        Next
        Return block
    End Function

    ''' <summary>Inverse of <see cref="ToGenderBlock"/>: expand an ARMA per-gender bone-scale block
    ''' (BSMS DELTAS) back into ABSOLUTE sclp entries (delta + 1.0). For export.</summary>
    Public Function FromGenderBlock(block As ARMA_BoneScaleGender) As List(Of SclpBoneAbsolute)
        Dim result As New List(Of SclpBoneAbsolute)
        If block Is Nothing OrElse block.Bones Is Nothing Then Return result
        For Each b In block.Bones
            If b Is Nothing Then Continue For
            result.Add(New SclpBoneAbsolute With {
                .Name = If(b.BoneName, ""),
                .X = b.DeltaX + 1.0F,
                .Y = b.DeltaY + 1.0F,
                .Z = b.DeltaZ + 1.0F
            })
        Next
        Return result
    End Function

    ' =====================================================================
    ' Self-test (invoked by Tools/SclpRoundTripProbe)
    ' =====================================================================

    ' ⛔ EL SELF-TEST YA NO VIVE ACA. Se mudó a Tools/SclpRoundTripProbe el 2026-08-08: eran 98 líneas
    ' `Public` dentro del exe que se distribuye —que además escribían archivos temporales— y NO las llamaba
    ' NADIE de la app, sólo esa probe. Es I/O de JSON: da lo mismo en toda máquina.
    ' Ver memoria 00-reglas-self-tests-no-van-en-el-binario.

End Module
