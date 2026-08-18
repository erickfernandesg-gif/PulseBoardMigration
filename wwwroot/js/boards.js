document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    initDragAndDrop();
    initAjaxForms();
    initTaskFormRules();
    initFilters();
    initGanttControls();
});

function antiforgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
}

function initTabs() {
    document.querySelectorAll('[data-view-target]').forEach(button => {
        button.addEventListener('click', () => {
            const target = button.dataset.viewTarget;
            ['kanban', 'table', 'gantt'].forEach(view =>
                document.getElementById(`view-${view}`)?.classList.toggle('hidden', view !== target));
            document.querySelectorAll('[data-view-target]').forEach(item => {
                item.classList.toggle('bg-white', item === button);
                item.classList.toggle('shadow-sm', item === button);
                item.classList.toggle('text-indigo-600', item === button);
            });
            if (target === 'gantt') {
                requestAnimationFrame(() => requestAnimationFrame(ensureGantt));
            }
        });
    });
}

let ganttAllTasks = [];
let ganttMode = 'Week';
let ganttResizeTimer;
let ganttInitialized = false;

function initGanttControls() {
    const dataElement = document.getElementById('gantt-data');
    if (!dataElement) return;
    try {
        ganttAllTasks = JSON.parse(dataElement.textContent || '[]');
    } catch {
        showGanttError('Não foi possível interpretar os dados do cronograma.');
        return;
    }

    document.querySelectorAll('[data-gantt-mode]').forEach(button => {
        button.addEventListener('click', () => changeGanttMode(button.dataset.ganttMode));
    });
    document.getElementById('ganttToday')?.addEventListener('click', () => {
        if (typeof window.ganttChartInstance?.scroll_current === 'function') {
            window.ganttChartInstance.scroll_current();
        }
    });
    window.addEventListener('resize', () => {
        if (document.getElementById('view-gantt')?.classList.contains('hidden')) return;
        clearTimeout(ganttResizeTimer);
        ganttResizeTimer = setTimeout(() => renderGantt(true), 180);
    });
}

function ensureGantt() {
    if (!document.getElementById('gantt-chart')) return;
    if (!ganttInitialized) renderGantt();
    else if (window.ganttChartInstance) window.ganttChartInstance.change_view_mode(ganttMode, true);
}

function filteredGanttTasks() {
    const month = document.getElementById('filterMonth')?.value || '';
    const user = document.getElementById('filterUser')?.value || '';
    return ganttAllTasks.filter(task => {
        const monthOk = !month || (month === 'inbox' ? !task.targetMonth : task.targetMonth === month);
        const collaborators = (task.collaborators || '').split(',').filter(Boolean);
        const userOk = !user || task.assignedTo === user || collaborators.includes(user);
        return monthOk && userOk;
    });
}

function renderGantt(maintainPosition = false) {
    const chart = document.getElementById('gantt-chart');
    const scroll = document.getElementById('gantt-scroll');
    const empty = document.getElementById('ganttEmpty');
    if (!chart || !scroll || !empty) return;
    if (typeof window.Gantt !== 'function') {
        showGanttError('A biblioteca do Gantt não foi carregada. Verifique a conexão com o CDN e atualize a página.');
        return;
    }

    const filteredTasks = filteredGanttTasks();
    const visibleIds = new Set(filteredTasks.map(task => task.id));
    const tasks = filteredTasks.map(task => ({
        ...task,
        dependencies: (task.dependencies || '').split(',').filter(id => visibleIds.has(id)).join(',')
    }));
    scroll.classList.toggle('hidden', tasks.length === 0);
    empty.classList.toggle('hidden', tasks.length > 0);
    if (tasks.length === 0) {
        chart.replaceChildren();
        window.ganttChartInstance = null;
        ganttInitialized = false;
        return;
    }

    const previousScroll = maintainPosition ? scroll.scrollLeft : 0;
    chart.replaceChildren();
    try {
        window.ganttChartInstance = new Gantt('#gantt-chart', tasks, {
            header_height: 50,
            column_width: ganttMode === 'Day' ? 38 : ganttMode === 'Month' ? 120 : 42,
            step: 24,
            view_mode: ganttMode,
            // Frappe Gantt 0.6.1 usa a chave ptBr; "pt" deixa a lista de meses
            // indefinida e causa "Cannot read properties of undefined (reading '0')".
            language: 'ptBr',
            on_date_change: (task, start, end) => persistGanttDates(task, start, end),
            custom_popup_html: task => `
                <div class="rounded-lg border border-slate-100 bg-white p-3 text-xs shadow-xl">
                    <div class="mb-1 font-bold text-slate-900">${escapeHtml(task.name)}</div>
                    <div class="text-slate-500">${escapeHtml(task.responsible || 'Sem responsável')}</div>
                    <div class="mt-2 text-slate-500">${formatGanttDate(task.start)} → ${formatGanttDate(task.end)}</div>
                    <div class="mt-1 font-bold text-indigo-600">Progresso: ${Math.round(task.progress || 0)}%</div>
                </div>`
        });
        ganttInitialized = true;
        hideGanttError();
        if (maintainPosition) scroll.scrollLeft = previousScroll;
    } catch (error) {
        ganttInitialized = false;
        showGanttError(error?.message || 'Não foi possível montar o cronograma.');
    }
}

async function persistGanttDates(task, start, end) {
    const original = ganttAllTasks.find(item => item.id === task.id);
    const startDate = ganttDateValue(start);
    const dueDate = ganttDateValue(end);
    if (!original || !startDate || !dueDate) return;
    try {
        const result = await postForm('/Boards/UpdateTaskSchedule', { taskId: task.id, startDate, dueDate });
        if (!result.success) throw new Error(result.message || 'Não foi possível reagendar a tarefa.');
        original.start = startDate;
        original.end = dueDate;
    } catch (error) {
        alert(error.message || 'Não foi possível reagendar a tarefa.');
        renderGantt(true);
    }
}

function changeGanttMode(mode) {
    if (!['Day', 'Week', 'Month'].includes(mode)) return;
    ganttMode = mode;
    document.querySelectorAll('[data-gantt-mode]').forEach(button => {
        const active = button.dataset.ganttMode === mode;
        button.classList.toggle('bg-white', active);
        button.classList.toggle('text-slate-900', active);
        button.classList.toggle('shadow-sm', active);
        button.classList.toggle('text-slate-600', !active);
    });
    if (window.ganttChartInstance) window.ganttChartInstance.change_view_mode(mode);
}

function refreshVisibleGantt() {
    if (!document.getElementById('view-gantt')?.classList.contains('hidden')) renderGantt();
}

function ganttDateValue(value) {
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function formatGanttDate(value) {
    const iso = ganttDateValue(value);
    if (!iso) return 'Sem data';
    const [year, month, day] = iso.split('-');
    return `${day}/${month}/${year}`;
}

function escapeHtml(value) {
    const element = document.createElement('span');
    element.textContent = value || '';
    return element.innerHTML;
}

function showGanttError(message) {
    const element = document.getElementById('ganttError');
    if (!element) return;
    element.textContent = message;
    element.classList.remove('hidden');
}

function hideGanttError() {
    document.getElementById('ganttError')?.classList.add('hidden');
}

function initDragAndDrop() {
    document.querySelectorAll('.kanban-column-body').forEach(column => {
        new Sortable(column, {
            group: 'pulseboard',
            animation: 150,
            ghostClass: 'opacity-40',
            onEnd: async event => {
                if (event.from === event.to && event.oldIndex === event.newIndex) return;
                const card = event.item;
                try {
                    const result = await postForm('/Boards/MoveTask', {
                        taskId: card.dataset.taskId,
                        newColumnId: event.to.dataset.columnId,
                        positionIndex: event.newIndex || 0
                    });
                    if (!result.success) throw new Error(result.message || 'Não foi possível mover a tarefa.');
                    card.dataset.status = event.to.dataset.columnId;
                } catch (error) {
                    event.from.insertBefore(card, event.from.children[event.oldIndex] || null);
                    alert(error.message || 'Não foi possível mover a tarefa.');
                }
            }
        });
    });
}

function initAjaxForms() {
    [
        'createTaskForm', 'editTaskForm', 'handoffTaskForm', 'deleteTaskForm',
        'dependencyForm', 'returnQuestionForm', 'checklistForm', 'commentForm', 'timeLogForm',
        'subtaskForm', 'taskFileForm'
    ].forEach(id => {
        const form = document.getElementById(id);
        if (!form) return;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            clearFormError(form);
            if (!validateTaskForm(form)) return;

            const button = form.querySelector('button[type="submit"]');
            if (button) button.disabled = true;
            try {
                const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
                const contentType = response.headers.get('content-type') || '';
                const result = contentType.includes('application/json')
                    ? await response.json()
                    : { success: false, message: 'O servidor retornou uma resposta inválida.' };
                if (!response.ok || !result.success) {
                    throw new Error(result.message || 'Operação não concluída.');
                }
                window.location.reload();
            } catch (error) {
                showFormError(form, error.message || 'Erro de comunicação.');
                if (button) button.disabled = false;
            }
        });
    });
}

function validateTaskForm(form) {
    if (form.id !== 'createTaskForm' && form.id !== 'editTaskForm') return true;

    const startDate = form.elements.namedItem('startDate')?.value;
    const dueDate = form.elements.namedItem('dueDate')?.value;
    if (startDate && dueDate && dueDate < startDate) {
        showFormError(form, 'O prazo não pode ser anterior à data de início.');
        form.elements.namedItem('dueDate')?.focus();
        return false;
    }

    const isBlocked = form.elements.namedItem('isBlocked');
    const blockerReason = form.elements.namedItem('blockerReason');
    if (isBlocked?.checked && !blockerReason?.value.trim()) {
        showFormError(form, 'Informe o motivo do bloqueio.');
        blockerReason.focus();
        return false;
    }

    return true;
}

function formErrorElement(form) {
    if (form.id === 'createTaskForm') return document.getElementById('createTaskError');
    if (form.id === 'editTaskForm') return document.getElementById('editTaskError');
    if (form.id === 'handoffTaskForm') return document.getElementById('handoffTaskError');
    return null;
}

function clearFormError(form) {
    const errorElement = formErrorElement(form);
    if (!errorElement) return;
    errorElement.textContent = '';
    errorElement.classList.add('hidden');
}

function showFormError(form, message) {
    const errorElement = formErrorElement(form);
    if (!errorElement) {
        alert(message);
        return;
    }
    errorElement.textContent = message;
    errorElement.classList.remove('hidden');
    errorElement.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function initTaskFormRules() {
    [['CreateStartDate', 'CreateDueDate'], ['EditStartDate', 'EditDueDate']]
        .forEach(([startId, dueId]) => {
            const start = document.getElementById(startId);
            const due = document.getElementById(dueId);
            start?.addEventListener('change', () => {
                due.min = start.value;
                if (due.value && due.value < start.value) due.value = '';
            });
        });

    const blocked = document.getElementById('EditIsBlocked');
    const reason = document.getElementById('EditBlockerReason');
    blocked?.addEventListener('change', () => {
        reason.required = blocked.checked;
        if (!blocked.checked) reason.value = '';
    });

    const chatImages = document.getElementById('ChatImages');
    chatImages?.addEventListener('change', () => renderChatImagePreview(chatImages.files));

    const template = document.getElementById('CreateTaskTemplate');
    template?.addEventListener('change', () => {
        const option = template.selectedOptions[0];
        const form = document.getElementById('createTaskForm');
        if (!form || !option?.value) return;
        const title = form.elements.namedItem('title');
        const description = form.elements.namedItem('description');
        const priority = form.elements.namedItem('priority');
        const hours = form.elements.namedItem('estimatedHours');
        const minutes = form.elements.namedItem('estimatedMinutes');
        const totalMinutes = Math.max(0, Number.parseInt(option.dataset.estimatedMinutes || '0', 10) || 0);
        if (title && !title.value.trim()) title.value = option.dataset.name || '';
        if (description) description.value = option.dataset.description || '';
        if (priority) priority.value = option.dataset.priority || 'medium';
        if (hours) hours.value = Math.floor(totalMinutes / 60).toString();
        if (minutes) minutes.value = (totalMinutes % 60).toString();
    });
}

function renderChatImagePreview(files) {
    const preview = document.getElementById('chatImagePreview');
    if (!preview) return;
    preview.querySelectorAll('img').forEach(image => URL.revokeObjectURL(image.src));
    preview.replaceChildren();
    const selected = [...(files || [])];
    preview.classList.toggle('hidden', selected.length === 0);
    preview.classList.toggle('grid', selected.length > 0);
    selected.slice(0, 4).forEach(file => {
        const wrapper = document.createElement('div');
        wrapper.className = 'overflow-hidden rounded-lg border border-indigo-100 bg-white';
        const image = document.createElement('img');
        image.src = URL.createObjectURL(file);
        image.alt = file.name;
        image.className = 'h-24 w-full object-cover';
        const name = document.createElement('p');
        name.className = 'truncate px-2 py-1 text-[10px] text-slate-500';
        name.textContent = file.name;
        wrapper.append(image, name);
        preview.append(wrapper);
    });
}

function initFilters() {
    const month = document.getElementById('filterMonth');
    const user = document.getElementById('filterUser');
    const apply = () => {
        document.querySelectorAll('.kanban-task, .task-filter-row').forEach(item => {
            const monthOk = !month?.value ||
                (month.value === 'inbox' ? !item.dataset.targetMonth : item.dataset.targetMonth === month.value);
            const userOk = !user?.value || item.dataset.assignedTo === user.value ||
                (item.dataset.collaborators || '').split(',').includes(user.value);
            item.classList.toggle('hidden', !(monthOk && userOk));
        });
    };
    month?.addEventListener('change', apply);
    user?.addEventListener('change', apply);
    month?.addEventListener('change', refreshVisibleGantt);
    user?.addEventListener('change', refreshVisibleGantt);
}

async function postForm(url, values) {
    const body = new URLSearchParams({ ...values, __RequestVerificationToken: antiforgeryToken() });
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body
    });
    const contentType = response.headers.get('content-type') || '';
    const result = contentType.includes('application/json')
        ? await response.json()
        : { success: false, message: 'O servidor retornou uma resposta inválida.' };
    if (!response.ok) throw new Error(result.message || `Operação recusada (${response.status}).`);
    return result;
}

window.openCreateTaskModal = columnId => {
    const modal = document.getElementById('createTaskModal');
    const form = document.getElementById('createTaskForm');
    form.reset();
    clearFormError(form);
    document.getElementById('CreateDueDate').min = '';
    const column = document.getElementById('CreateColumnId');
    const requestedColumn = columnId || 'todo';
    column.value = [...column.options].some(option => option.value === requestedColumn)
        ? requestedColumn
        : column.options[0]?.value || '';
    modal.classList.remove('hidden');
    modal.classList.add('flex');
    form.elements.namedItem('title')?.focus();
};

window.closeCreateTaskModal = () => {
    const modal = document.getElementById('createTaskModal');
    modal.classList.add('hidden');
    modal.classList.remove('flex');
};

window.openTaskDetailsModal = element => {
    const get = name => element.dataset[name] || '';
    const taskId = get('taskId');
    const editForm = document.getElementById('editTaskForm');
    editForm.reset();
    clearFormError(editForm);
    const handoffForm = document.getElementById('handoffTaskForm');
    handoffForm?.reset();
    if (handoffForm) clearFormError(handoffForm);
    document.getElementById('EditTaskId').value = taskId;
    document.getElementById('EditExpectedVersion').value = get('rowVersion');
    document.getElementById('EditTitle').value = get('title');
    document.getElementById('EditDescription').value = get('description');
    document.getElementById('EditColumnId').value = get('status');
    document.getElementById('EditPriority').value = get('priority') || 'medium';
    document.getElementById('EditAssignedTo').value = get('assignedTo');
    document.getElementById('EditClientId').value = get('clientId');
    document.getElementById('EditStartDate').value = get('startDate');
    document.getElementById('EditDueDate').value = get('dueDate');
    document.getElementById('EditDueDate').min = get('startDate');
    document.getElementById('EditTargetMonth').value = get('targetMonth');
    const estimated = Number(get('estimatedMinutes') || 0);
    document.getElementById('EditEstimatedHours').value = Math.floor(estimated / 60);
    document.getElementById('EditEstimatedMinutes').value = estimated % 60;
    document.getElementById('EditSlaMinutes').value = get('slaMinutes');
    document.getElementById('EditPlannedValue').value = get('plannedValue');
    document.getElementById('EditCustomFields').value = get('customFields') || '{}';
    document.getElementById('EditIsBlocked').checked = get('isBlocked') === 'true';
    document.getElementById('EditBlockerReason').value = get('blockerReason');
    document.getElementById('EditBlockerReason').required = get('isBlocked') === 'true';
    const collaborators = get('collaborators').split(',').filter(Boolean);
    document.querySelectorAll('.edit-collaborator').forEach(input =>
        input.checked = collaborators.includes(input.value));
    document.querySelectorAll('.task-id-target').forEach(input => input.value = taskId);
    document.querySelectorAll('.dependency-task-option').forEach(option => {
        option.disabled = option.value === taskId;
        option.hidden = option.value === taskId;
    });
    const previousFile = document.getElementById('PreviousTaskFile');
    if (previousFile) previousFile.value = '';
    document.querySelectorAll('.task-file-version-option').forEach(option => {
        const visible = option.dataset.taskId === taskId;
        option.disabled = !visible;
        option.hidden = !visible;
    });
    renderChatImagePreview([]);
    const template = document.getElementById(`task-extra-${taskId}`);
    document.getElementById('taskExtraContent').innerHTML = template?.innerHTML || '';
    const modal = document.getElementById('taskDetailsModal');
    modal.classList.remove('hidden');
    modal.classList.add('flex');
    lucide?.createIcons();
};

window.closeTaskDetailsModal = () => {
    const modal = document.getElementById('taskDetailsModal');
    modal.classList.add('hidden');
    modal.classList.remove('flex');
};

window.confirmDeleteTask = () => {
    if (!confirm('Arquivar esta tarefa? Conversas, horas e histórico serão preservados.')) return;
    document.getElementById('DeleteTaskId').value = document.getElementById('EditTaskId').value;
    document.getElementById('deleteTaskForm').requestSubmit();
};

window.restoreTask = async taskId => {
    try {
        const result = await postForm('/Boards/RestoreTask', { taskId });
        if (!result.success) throw new Error(result.message || 'Não foi possível restaurar a tarefa.');
        window.location.reload();
    } catch (error) { alert(error.message || 'Não foi possível restaurar a tarefa.'); }
};

window.toggleChecklist = async (id, completed) => {
    const result = await postForm('/Boards/ToggleChecklistItem', { id, completed });
    if (!result.success) alert('Não foi possível atualizar o item.');
};

window.deleteChecklist = async id => {
    if (!confirm('Excluir este item?')) return;
    const result = await postForm('/Boards/DeleteChecklistItem', { id });
    if (result.success) window.location.reload();
};

window.editComment = async (commentId, currentContent) => {
    const content = prompt('Editar comentário:', currentContent);
    if (!content?.trim()) return;
    const result = await postForm('/Boards/UpdateComment', { commentId, content });
    if (result.success) window.location.reload();
    else alert('Não foi possível editar o comentário.');
};

window.deleteComment = async commentId => {
    if (!confirm('Excluir este comentário?')) return;
    const result = await postForm('/Boards/DeleteComment', { commentId });
    if (result.success) window.location.reload();
    else alert('Não foi possível excluir o comentário.');
};

window.deleteDependency = async dependencyId => {
    if (!confirm('Remover este pré-requisito?')) return;
    const result = await postForm('/Work/DeleteDependency', { dependencyId });
    if (result.success) window.location.reload();
    else alert(result.message || 'Não foi possível remover o pré-requisito.');
};
