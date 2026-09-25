document.addEventListener('DOMContentLoaded', () => {
    const antiforgeryToken = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const formDataWithAntiforgery = form => {
        const data = new FormData(form);
        const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || antiforgeryToken();
        if (token) data.set('__RequestVerificationToken', token);
        return data;
    };
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
        row.className = 'column-row grid items-end gap-3 rounded-xl bg-slate-50 p-3 md:grid-cols-[minmax(220px,1fr)_92px_140px_minmax(160px,.8fr)_auto]';
        row.innerHTML = `<input type="hidden" name="columnId" value="stage-${Date.now()}"/><label class="block"><span class="mb-1 block text-[10px] font-bold uppercase tracking-wide text-slate-500">Nome exibido no quadro</span><input name="title" value="Nova etapa" class="field" aria-label="Nome exibido no quadro"/><span class="mt-1 block text-[10px] text-slate-400">Ex.: Planejado, Em andamento ou Em teste.</span></label><label class="block"><span class="mb-1 block text-[10px] font-bold uppercase tracking-wide text-slate-500">Cor</span><input name="color" type="color" value="#6366f1" class="h-10 w-full rounded border" aria-label="Cor da etapa"/></label><label class="block"><span class="mb-1 block text-[10px] font-bold uppercase tracking-wide text-slate-500">Limite de tarefas</span><input name="wipLimit" type="number" min="1" class="field" placeholder="Sem limite" aria-label="Limite de tarefas nesta etapa"/></label><label class="flex min-h-10 items-center gap-2 text-sm"><input type="checkbox" name="requiresApproval" value="${index}"/> Solicitar validação nesta etapa</label><div class="flex h-10 items-center"><button type="button" class="column-up p-2 text-slate-400" title="Mover etapa para cima" aria-label="Mover etapa para cima">↑</button><button type="button" class="column-down p-2 text-slate-400" title="Mover etapa para baixo" aria-label="Mover etapa para baixo">↓</button><button type="button" class="column-remove p-2 text-red-500" title="Remover etapa" aria-label="Remover etapa">×</button></div>`;
        editor.append(row); wireColumn(row); syncColumnIndexes();
    });

    document.getElementById('selectAllTasks')?.addEventListener('change', event =>
        document.querySelectorAll('#bulkForm input[name="taskIds"]').forEach(input => input.checked = event.target.checked));
    const bulkAction = document.getElementById('bulkAction');
    const bulkHelp = document.getElementById('bulkActionHelp');
    const bulkMessages = {
        assign: 'Escolha o novo responsável. “Sem responsável” remove a atribuição das tarefas selecionadas.',
        move: 'Escolha a etapa de destino. O limite WIP e as regras de aprovação continuam sendo respeitados.',
        archive: 'Arquiva as tarefas selecionadas. Elas saem do Board, mas o histórico é preservado e podem ser restauradas depois.',
        due_date: 'Escolha o novo prazo que será aplicado a todas as tarefas selecionadas.',
        priority: 'Escolha a nova prioridade que será aplicada a todas as tarefas selecionadas.'
    };
    const syncBulkFields = () => {
        document.querySelectorAll('[data-bulk-field]').forEach(field => {
            const visible = field.dataset.bulkField === bulkAction?.value;
            field.classList.toggle('hidden', !visible);
            field.disabled = !visible;
        });
        if (bulkHelp) bulkHelp.textContent = bulkMessages[bulkAction?.value] || '';
    };
    bulkAction?.addEventListener('change', syncBulkFields); syncBulkFields();
    document.getElementById('bulkForm')?.addEventListener('submit', async event => {
        event.preventDefault();
        const error = document.getElementById('bulkError');
        error?.classList.add('hidden');
        const selected = event.target.querySelectorAll('input[name="taskIds"]:checked');
        if (!selected.length) { if (error) { error.textContent = 'Selecione ao menos uma tarefa.'; error.classList.remove('hidden'); } return; }
        if (bulkAction?.value === 'archive' && !confirm(`Arquivar ${selected.length} tarefa(s)? O histórico será preservado, mas elas deixarão de aparecer no Board.`)) return;
        const button = event.target.querySelector('button[type="submit"], button:not([type])'); if (button) button.disabled = true;
        try {
            const response = await fetch(event.target.action, { method: 'POST', body: formDataWithAntiforgery(event.target) });
            const contentType = response.headers.get('content-type') || '';
            const result = contentType.includes('application/json')
                ? await response.json()
                : { success: false, message: 'A sessão ou a validação de segurança expirou. Atualize a página e tente novamente.' };
            if (!response.ok || !result.success) throw new Error(result.message || 'Falha na operação.');
            location.reload();
        } catch (exception) {
            if (error) { error.textContent = exception.message; error.classList.remove('hidden'); }
            if (button) button.disabled = false;
        }
    });

    const mirrorTargetBoard = document.getElementById('mirrorTargetBoard');
    const mirrorTargetTask = document.getElementById('mirrorTargetTask');
    mirrorTargetBoard?.addEventListener('change', () => {
        const boardId = mirrorTargetBoard.value;
        if (!mirrorTargetTask) return;
        mirrorTargetTask.value = '';
        mirrorTargetTask.disabled = !boardId;
        mirrorTargetTask.querySelectorAll('option[data-board-id]').forEach(option => {
            option.hidden = option.dataset.boardId !== boardId;
        });
        mirrorTargetTask.options[0].textContent = boardId
            ? 'Selecione a tarefa de destino'
            : 'Escolha primeiro o projeto de destino';
    });

    const dependencyTargetBoard = document.getElementById('dependencyTargetBoard');
    const dependencyTargetTask = document.getElementById('dependencyTargetTask');
    dependencyTargetBoard?.addEventListener('change', () => {
        const boardId = dependencyTargetBoard.value;
        if (!dependencyTargetTask) return;
        dependencyTargetTask.value = '';
        dependencyTargetTask.disabled = !boardId;
        dependencyTargetTask.querySelectorAll('option[data-board-id]').forEach(option => {
            option.hidden = option.dataset.boardId !== boardId;
        });
        dependencyTargetTask.options[0].textContent = boardId
            ? 'Selecione a tarefa pré-requisito'
            : 'Escolha primeiro o projeto externo';
    });

    document.getElementById('crossDependencyForm')?.addEventListener('submit', event => {
        const form = event.currentTarget;
        const blocked = form.elements.namedItem('taskId')?.selectedOptions?.[0]?.textContent?.trim() || 'a tarefa deste projeto';
        const prerequisite = form.elements.namedItem('dependsOnTaskId')?.selectedOptions?.[0]?.textContent?.trim() || 'o pré-requisito';
        if (!confirm(`Confirmar dependência?\n\n“${blocked}” só poderá ser concluída depois que “${prerequisite}” for concluída.`)) event.preventDefault();
    });

    document.getElementById('mirrorForm')?.addEventListener('submit', event => {
        const form = event.currentTarget;
        const source = form.elements.namedItem('sourceTaskId')?.selectedOptions?.[0]?.textContent?.trim() || 'a tarefa de origem';
        const target = form.elements.namedItem('targetTaskId')?.selectedOptions?.[0]?.textContent?.trim() || 'a tarefa de destino';
        const field = form.elements.namedItem('fieldName')?.selectedOptions?.[0]?.textContent?.trim() || 'o campo escolhido';
        if (!confirm(`Confirmar espelhamento de ${field}?\n\nSempre que “${source}” mudar, “${target}” será atualizada automaticamente.`)) event.preventDefault();
    });
});
