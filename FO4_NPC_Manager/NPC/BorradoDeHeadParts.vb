Imports System.Windows.Forms

''' <summary>EL CAMINO DE BAJA de las tres clases de borrador que trae la ola de head parts —HDPT, TXST
''' y FLST—, en UN solo lugar y compartido por los editores que las pueden elegir.
'''
''' <para>⛔ <b>Existe porque ofrecer un borrador en un picker SIN camino de baja lo deja sin salida.</b>
''' Es la mitad obligatoria que el ⛔ de <c>ArmaEditor_Form.PickFidInto</c> ya declara para los material
''' swap: «sin esto el botón "Delete / Revert…" ni se ve, y el MSWP queda sin salida después del OK». Y
''' acá pesa más, porque la fase de guardado emite TODO borrador sucio, referenciado o no: un head part
''' arrepentido se escribiría igual, y un override de un HDPT vanilla le cambia la cabeza a <b>todos</b>
''' los NPC que lo usan, no sólo al que se estaba editando.</para>
'''
''' <para>La ley es la MISMA que la de ARMO/ARMA/MSWP y por eso se escribe una vez: un OVERRIDE se
''' REVIERTE (se baja el borrador y se marca el record para que la preservación no lo vuelva a escribir,
''' así gana el original); un borrador NUEVO se BORRA, y sólo si no lo referencia nadie —el censo es
''' <c>MainForm.GetDraftReferrers</c>, la fuente única, que desde esta ola mira las OCHO clases—; y un
''' record YA GUARDADO se marca para quitar en el próximo guardado.</para>
'''
''' <para>⚠️ Lo que NO se copia del molde de MSWP: acá el record SÍ puede ser el que está abierto en el
''' editor. Un HDPT es el sujeto del editor de head parts, no el valor de uno de sus campos — así que el
''' llamador que esté editando ESE FormID tiene que cerrarse o re-apuntar, y por eso
''' <see cref="BorrarORevertir"/> devuelve True recién cuando la baja ocurrió.</para></summary>
Friend Module BorradoDeHeadParts

    ''' <summary>Da de baja el HDPT / TXST / FLST de <paramref name="entry"/> según su clase. True si se
    ''' dio de baja (el selector saca la fila), False si no se hizo nada — cancelado, bloqueado por
    ''' referencias, o la fila no es de una clase que este camino sepa bajar.</summary>
    Friend Function BorrarORevertir(mainForm As MainForm, owner As IWin32Window,
                                    entry As FormIdPickerEntry) As Boolean
        If entry Is Nothing OrElse mainForm Is Nothing Then Return False
        Dim fid = entry.FormID
        If fid = 0UI Then Return False

        ' ---- ¿es un borrador vivo de alguna de las tres clases?
        Dim hd = mainForm.HdptDraftPorFormId(fid)
        If hd IsNot Nothing Then Return BajarBorrador(mainForm, owner, fid, hd.IsNew, hd.Record.EditorID,
                                                      "head part", AddressOf mainForm.UnregisterHdptDraft,
                                                      "A head part is SHARED: this affects every NPC in the load order that wears it.")
        Dim tx = mainForm.TxstDraftPorFormId(fid)
        If tx IsNot Nothing Then Return BajarBorrador(mainForm, owner, fid, tx.IsNew, tx.Record.EditorID,
                                                      "texture set", AddressOf mainForm.UnregisterTxstDraft,
                                                      "A texture set is SHARED: this affects everything that points at it.")
        Dim fl = mainForm.FlstDraftPorFormId(fid)
        If fl IsNot Nothing Then Return BajarBorrador(mainForm, owner, fid, fl.IsNew, fl.Record.EditorID,
                                                      "form list", AddressOf mainForm.UnregisterFlstDraft,
                                                      "A form list is SHARED: this affects everything that points at it.")

        ' ---- ya GUARDADO en el plugin: se marca para quitar en el próximo guardado.
        Dim clase = If(String.IsNullOrEmpty(entry.Signature), "record", NombreDeLaClase(entry.Signature))
        Dim esNuevo = entry.EditorID IsNot Nothing AndAlso
                      entry.EditorID.StartsWith("npcm_", StringComparison.OrdinalIgnoreCase)
        Dim verbo = If(esNuevo, "Delete", "Revert")
        Dim detalle = If(esNuevo, "It will be removed from your plugin on the next Save.",
                                  "The override will be dropped on the next Save — the original record wins again.")
        Dim refs = mainForm.GetDraftReferrers(fid)
        Dim aviso = If(refs.Count > 0, vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf & String.Join(vbCrLf, refs), "")
        If MessageBox.Show(owner,
                           $"{verbo} saved {clase} '{entry.DisplayName}'?" & vbCrLf & detalle & aviso,
                           $"{verbo} saved {clase}", MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
        mainForm.MarkRecordForRemoval(fid)
        mainForm.RevertAppOverrideInMemory(fid)
        Return True
    End Function

    ''' <summary>La baja de un borrador VIVO, que es la misma para las tres clases: revertir si es
    ''' override, borrar si es nuevo y nadie lo apunta.</summary>
    Private Function BajarBorrador(mainForm As MainForm, owner As IWin32Window, fid As UInteger,
                                   esNuevo As Boolean, edid As String, clase As String,
                                   bajar As Action(Of UInteger), advertenciaDeCompartido As String) As Boolean
        If Not esNuevo Then
            If MessageBox.Show(owner,
                               $"Revert {clase} '{edid}' to the original record?" & vbCrLf &
                               "Your edits will be discarded." & vbCrLf & vbCrLf & advertenciaDeCompartido,
                               $"Revert {clase}", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) <> DialogResult.Yes Then Return False
            bajar(fid)
            mainForm.MarkRecordForRemoval(fid)
            mainForm.RevertAppOverrideInMemory(fid)
            Return True
        End If

        Dim referrers = mainForm.GetDraftReferrers(fid)
        If referrers.Count > 0 Then
            MessageBox.Show(owner,
                            $"Can't delete — this {clase} is still referenced by:" & vbCrLf & vbCrLf &
                            String.Join(vbCrLf, referrers),
                            $"Delete {clase}", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End If
        If MessageBox.Show(owner, $"Delete {clase} draft '{edid}'? This cannot be undone.",
                           $"Delete {clase}", MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning) <> DialogResult.Yes Then Return False
        bajar(fid)
        Return True
    End Function

    Private Function NombreDeLaClase(firma As String) As String
        Select Case firma.ToUpperInvariant()
            Case "HDPT" : Return "head part"
            Case "TXST" : Return "texture set"
            Case "FLST" : Return "form list"
            Case Else : Return firma
        End Select
    End Function

End Module
