Imports System.Windows.Forms
Imports System.Drawing

''' <summary>Shared builder for the granular FO4 biped-slot (BOD2) checkbox grid used by BOTH the ARMA
''' editor (<see cref="ArmaEditor_Form"/>) and the ARMO editor (<see cref="ArmoEditor_Form"/>). Holds the
''' SINGLE xEdit-named slot-name table (slots 30..61, FO4 BOD2 bit = slot − 30) so neither editor
''' duplicates it.
'''
''' Each editor declares an empty <see cref="FlowLayoutPanel"/> in its Designer and calls
''' <see cref="Build"/> in code-behind (the "many repeated controls → Designer container + code-behind
''' children" rule), passing a CheckedChanged handler. The returned dictionary maps slot number → CheckBox
''' so the editor can <see cref="ReadMask"/> / <see cref="SetMask"/> the BOD2 value.
'''
''' Source spec (format, NOT game data): wbDefinitionsFO4.pas:3745-3778 wbBipedObjectFlags; same (N-30)
''' bit convention as <see cref="BipedSlots"/>.</summary>
Public Module BipedSlotCheckboxes

    ''' <summary>The full xEdit-named biped slot range 30..61 (FO4 BOD2 bit = slot − 30).</summary>
    Public ReadOnly AllSlots As Integer() = {
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45,
        46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61}

    ''' <summary>Populate <paramref name="flow"/> with one AutoSize checkbox per biped slot (xEdit-named),
    ''' wiring <paramref name="onChanged"/> to each. Returns a slot→CheckBox map for mask read/write.</summary>
    Public Function Build(flow As FlowLayoutPanel, onChanged As EventHandler) As Dictionary(Of Integer, CheckBox)
        Dim map As New Dictionary(Of Integer, CheckBox)
        For Each slot In AllSlots
            Dim cb As New CheckBox With {.AutoSize = True, .Text = SlotName(slot), .Margin = New Padding(3),
                                         .Tag = slot, .UseVisualStyleBackColor = True, .Width = 150}
            If onChanged IsNot Nothing Then AddHandler cb.CheckedChanged, onChanged
            flow.Controls.Add(cb)
            map(slot) = cb
        Next
        Return map
    End Function

    ''' <summary>Check the boxes whose (slot − 30) bit is set in <paramref name="mask"/>.</summary>
    Public Sub SetMask(map As Dictionary(Of Integer, CheckBox), mask As UInteger)
        For Each kv In map
            Dim bit = kv.Key - 30
            kv.Value.Checked = (bit >= 0 AndAlso bit < 32 AndAlso (mask And (1UI << bit)) <> 0UI)
        Next
    End Sub

    ''' <summary>Build the BOD2 mask from the checked boxes ((slot − 30) bit set per checked slot).</summary>
    Public Function ReadMask(map As Dictionary(Of Integer, CheckBox)) As UInteger
        Dim mask As UInteger = 0UI
        For Each kv In map
            If kv.Value.Checked Then
                Dim bit = kv.Key - 30
                If bit >= 0 AndAlso bit < 32 Then mask = mask Or (1UI << bit)
            End If
        Next
        Return mask
    End Function

    ''' <summary>xEdit FO4 biped slot name (slots 30..61). SINGLE source — do not duplicate this table.</summary>
    Public Function SlotName(slot As Integer) As String
        Select Case slot
            Case 30 : Return "30 Hair Top"
            Case 31 : Return "31 Hair Long"
            Case 32 : Return "32 FaceGen Head"
            Case 33 : Return "33 BODY"
            Case 34 : Return "34 L Hand"
            Case 35 : Return "35 R Hand"
            Case 36 : Return "36 [U] Torso"
            Case 37 : Return "37 [U] L Arm"
            Case 38 : Return "38 [U] R Arm"
            Case 39 : Return "39 [U] L Leg"
            Case 40 : Return "40 [U] R Leg"
            Case 41 : Return "41 [A] Torso"
            Case 42 : Return "42 [A] L Arm"
            Case 43 : Return "43 [A] R Arm"
            Case 44 : Return "44 [A] L Leg"
            Case 45 : Return "45 [A] R Leg"
            Case 46 : Return "46 Headband"
            Case 47 : Return "47 Eyes"
            Case 48 : Return "48 Beard"
            Case 49 : Return "49 Mouth"
            Case 50 : Return "50 Neck"
            Case 51 : Return "51 Ring"
            Case 52 : Return "52 Scalp"
            Case 53 : Return "53 Decapitation"
            Case 54 : Return "54 Modular 54"
            Case 55 : Return "55 Modular 55"
            Case 56 : Return "56 Modular 56"
            Case 57 : Return "57 Modular 57"
            Case 58 : Return "58 Modular 58"
            Case 59 : Return "59 Modular 59"
            Case 60 : Return "60 Modular 60"
            Case 61 : Return "61 FX01"
            Case Else : Return "s" & slot.ToString()
        End Select
    End Function

End Module
