Imports FO4_Base_Library

''' <summary>Editor modal de UN Include (una referencia a OMOD) de una combinación OBTS. Se abre
''' desde la pestaña Includes del editor de combinaciones (Add / Edit / doble clic en una fila).
'''
''' <para>El Include que recibe es el de TRABAJO: cuelga de la combinación de trabajo que armó el
''' que llama, así que se edita en el lugar y sólo al aceptar. Cancelar no le escribe nada.</para>
'''
''' <para>Reemplazó a la grilla GridIncludes editable en la celda (casillas Optional/DontUseAll +
''' celda de texto para el Attach Point Index + doble clic para re-elegir el OMOD), que era la que
''' provocaba el cuelgue por reentrada.</para></summary>
Public Class ObtsIncludeEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _inc As Canon.IBloque_Includes
    Private _omodFormID As UInteger

    ''' <param name="mainForm">Dueño: aporta el PluginManager para el selector de OMOD y los
    ''' nombres.</param>
    ''' <param name="inc">El Include de trabajo. Se escribe recién al aceptar.</param>
    Public Sub New(mainForm As MainForm, inc As Canon.IBloque_Includes)
        InitializeComponent()
        _mainForm = mainForm
        _inc = inc

        If _inc IsNot Nothing Then
            _omodFormID = _inc.IncludeMod
            Dim ap = CDec(_inc.IncludeAttachPointIndex)
            NumAttach.Value = Math.Max(NumAttach.Minimum, Math.Min(NumAttach.Maximum, ap))
            CheckOptional.Checked = _inc.IncludeOptional
            CheckDontUseAll.Checked = _inc.IncludeDonTUseAll
        End If
        RenderOmod()

        AddHandler ButtonPickOmod.Click, AddressOf OnPickOmod
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderOmod()
        LabelOmodValue.Text = If(_omodFormID = 0UI, "(none)",
                                 $"{_mainForm.GetRecordDisplayNameForEditor(_omodFormID)} " &
                                 $"[0x{_omodFormID:X8}]")
    End Sub

    Private Sub OnPickOmod(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"OMOD"},
                                           "Select OMOD include", _omodFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _omodFormID = dlg.SelectedFormID
            RenderOmod()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _inc IsNot Nothing Then
            _inc.IncludeMod = _omodFormID
            _inc.IncludeAttachPointIndex = CByte(NumAttach.Value)
            _inc.IncludeOptional = CheckOptional.Checked
            _inc.IncludeDonTUseAll = CheckDontUseAll.Checked
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
