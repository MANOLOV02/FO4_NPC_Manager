Imports System.Drawing
Imports System.Windows.Forms

''' <summary>H-1.10 — el aviso de que el NPC va a DEJAR DE HEREDAR.
'''
''' <para>⛔ Va en el COMMIT, no al abrir el editor: al abrir todavía no se sabe si el usuario va a cambiar
''' algo, y un aviso que salta «por las dudas» enseña a apagarlo — que es la peor forma de perderlo.</para>
'''
''' <para>⛔ «No» es ROLLBACK ENTERO, igual que Cancel: la puerta restaura el overlay anterior y no toca ni el
''' bit ni la caché. No hay un tercer desenlace de «aplicalo pero sin desprender», porque ese estado es el
''' defecto que toda esta ola vino a cerrar: la edición se vería en pantalla y el motor se la pisaría al
''' cargar.</para>
'''
''' <para>El tilde de «no me avises más» vive en un campo de SESIÓN del MainForm y no se persiste a disco. Una
''' preferencia persistente para un aviso de pérdida de herencia es un aviso que nadie vuelve a ver.</para>
'''
''' <para>Se arma por código y no por Designer: son seis controles y ningún handler, así que un .Designer.vb
''' sería más superficie que el diálogo.</para></summary>
Friend Class DetachWarningDialog
    Inherits Form

    Private ReadOnly _chkNoAvisar As CheckBox

    ''' <summary>El usuario pidió que no se le avise más en esta sesión.</summary>
    Friend ReadOnly Property NoVolverAAvisar As Boolean
        Get
            Return _chkNoAvisar.Checked
        End Get
    End Property

    Friend Sub New(etiquetaDelNpc As String)
        Text = "This NPC will stop inheriting"
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        ClientSize = New Size(520, 232)

        Dim lbl As New Label With {
            .AutoSize = False,
            .Location = New Point(14, 14),
            .Size = New Size(492, 132),
            .Text = etiquetaDelNpc & Environment.NewLine & Environment.NewLine &
                    "This NPC currently inherits its appearance from a template." & Environment.NewLine &
                    "Saving these changes will make it STOP INHERITING: it keeps a copy of what you are " &
                    "seeing now, and later edits to the template will no longer reach it." &
                    Environment.NewLine & Environment.NewLine &
                    "Choosing No discards EVERYTHING this editor session changed, not just the " &
                    "detachment. Until you save, Reset puts the NPC back the way it was — but Reset also " &
                    "discards every other pending change to it, and once saved the plugin already has the " &
                    "detached copy."
        }
        Controls.Add(lbl)

        _chkNoAvisar = New CheckBox With {
            .AutoSize = True,
            .Location = New Point(16, 154),
            .Text = "Do not warn me again in this session"
        }
        Controls.Add(_chkNoAvisar)

        Dim btnNo As New Button With {
            .Text = "No", .DialogResult = DialogResult.No,
            .Location = New Point(420, 190), .Size = New Size(86, 28)
        }
        Dim btnSi As New Button With {
            .Text = "Yes", .DialogResult = DialogResult.Yes,
            .Location = New Point(326, 190), .Size = New Size(86, 28)
        }
        Controls.Add(btnSi)
        Controls.Add(btnNo)

        AcceptButton = btnSi
        ' ⛔ El botón de Escape es «No», no «Yes»: cerrar con Esc o con la X tiene que ser el desenlace que NO
        ' cambia nada. Si el AcceptButton fuera también el CancelButton, un Esc distraído desprendería el NPC.
        CancelButton = btnNo
    End Sub
End Class
