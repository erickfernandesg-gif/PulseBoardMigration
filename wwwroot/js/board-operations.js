document.addEventListener('DOMContentLoaded', () => {
    const editor = document.getElementById('columnEditor');
    const syncColumnIndexes = () => {
        editor?.querySelectorAll('.column-row').forEach((row, index) => {
            const checkbox = row.querySelector('input[name="requiresApproval"]');
            if (checkbox) checkbox.value = String(index);
        });
    };
    const wireColumn = row => {
        row.querySelector('.column-up')?.addEventListener('click', () => { if (row.previousElementSibling) editor.insertBefore(row, row.previousElementSibling); syncColumnIndexes(); });
        row.querySelector('.column-down')?.addEventListener('click', () => { if (row.nextElementSibling) editor.insertBefore(row.nextElementSibling, row); syncColumnIndexes(); });
        row.querySelector('.column-remove')?.addEventListener('click', () => { if (editor.children.length <= 1) return alert('O Board precisa ter ao menos uma etapa.'); row.remove(); syncColumnIndexes(); });
    };
    editor?.querySelectorAll('.column-row').forEach(wireColumn);
    document.getElementById('addColumn')?.addEventListener('click', () => {
        if (!editor || editor.children.length >= 20) return alert('O limite é de 20 etapas por Board.');
        const index = editor.children.length;
        const row = document.createElement('div');
        row.className = 'column-row grid items-center gap-2 rounded-xl bg-slate-50 p-3 md:grid-cols-[1fr_1.3fr_80px_130px_1fr_auto]';
        row.innerHTML = `<input name="columnId" value="stage-${index + 1}" class="field" aria-label="Identificador"/><input name="title" value="Nova etapa" class="field" aria-label="Nome"/><input name="color" type="color" value="#6366f1" class="h-10 w-full rounded border" aria-label="Cor"/><input name="wipLimit" type="number" min="1" class="field" placeholder="Sem limite"/><label class="flex items-center gap-2 text-sm"><input type="checkbox" name="requiresApproval" value="${index}"/> Exigir aprovação</label><div class="flex"><button type="button" class="column-up p-2 text-slate-400">↑</button><button type="button" class="column-down p-2 text-slate-400">↓</button><button type="button" class="column-remove p-2 text-red-500">×</button></div>`;
        editor.append(row); wireColumn(row); syncColumnIndexes();
    });

    document.getElementById('selectAllTasks')?.addEventListener('change', event =>
        document.querySelectorAll('#bulkForm input[name="taskIds"]').forEach(input => input.checked = event.target.checked));
    const bulkAction = document.getElementById('bulkAction');
    const syncBulkFields = () => document.querySelectorAll('[data-bulk-field]').forEach(field => {
        const visible = field.dataset.bulkField === bulkAction?.value;
        field.classList.toggle('opacity-40', !visible); field.disabled = !visible;
    });
    bulkAction?.addEventListener('change', syncBulkFields); syncBulkFields();
    document.getElementById('bulkForm')?.addEventListener('submit', async event => {
        event.preventDefault();
        const error = document.getElementById('bulkError');
        error?.classList.add('hidden');
        if (!event.target.querySelector('input[name="taskIds"]:checked')) { if (error) { error.textContent = 'Selecione ao menos uma tarefa.'; error.classList.remove('hidden'); } return; }
        const button = event.target.querySelector('button[type="submit"], button:not([type])'); if (button) button.disabled = true;
        try {
            const response = await fetch(event.target.action, { method: 'POST', body: new FormData(event.target) });
            const result = await response.json();
            if (!response.ok || !result.success) throw new Error(result.message || 'Falha na operação.');
            location.reload();
        } catch (exception) {
            if (error) { error.textContent = exception.message; error.classList.remove('hidden'); }
            if (button) button.disabled = false;
        }
    });
});
