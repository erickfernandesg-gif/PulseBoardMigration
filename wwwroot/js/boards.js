document.addEventListener('DOMContentLoaded', () => {
    initTabs();
    initTaskDetailTabs();
    initDragAndDrop();
    initAjaxForms();
    initTaskFormRules();
    initHandoffValidation();
    initFilters();
    initGanttControls();
    if (!restoreTaskDetailsAfterReload()) openTaskDetailsFromUrl();
});

function antiforgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
}

function formDataWithAntiforgery(form) {
    const body = new FormData(form);
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value || antiforgeryToken();
    if (token) body.set('__RequestVerificationToken', token);
    return body;
}

async function jsonResponse(response) {
    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) return await response.json();

    if (response.status === 400 || response.status === 401 || response.status === 403 || response.redirected) {
        return { success: false, message: 'Sua sessão ou validação de segurança expirou. Recarregue a página e tente novamente.' };
    }
    return { success: false, message: `O servidor não concluiu a operação (${response.status || 'sem resposta'}).` };
}

function initTabs() {
    const requestedView = new URLSearchParams(window.location.search).get('view');
    const initialView = ['kanban', 'table', 'gantt'].includes(requestedView) ? requestedView : 'kanban';
    selectBoardView(initialView);

    document.querySelectorAll('[data-view-target]').forEach(button => {
        button.addEventListener('click', () => {
            const target = button.dataset.viewTarget;
            selectBoardView(target);
            persistBoardViewState();
        });
    });
}

function selectBoardView(target) {
    const view = ['kanban', 'table', 'gantt'].includes(target) ? target : 'kanban';
    ['kanban', 'table', 'gantt'].forEach(name =>
        document.getElementById(`view-${name}`)?.classList.toggle('hidden', name !== view));
    document.querySelectorAll('[data-view-target]').forEach(button => {
        const active = button.dataset.viewTarget === view;
        button.classList.toggle('bg-white', active);
        button.classList.toggle('shadow-sm', active);
        button.classList.toggle('text-indigo-600', active);
        button.classList.toggle('text-slate-600', !active);
    });
    if (view === 'gantt') {
        requestAnimationFrame(() => requestAnimationFrame(ensureGantt));
    }
}

function persistBoardViewState() {
    const url = new URL(window.location.href);
    const currentView = [...document.querySelectorAll('[data-view-target]')]
        .find(button => !button.classList.contains('text-slate-600'))?.dataset.viewTarget || 'kanban';
    const month = document.getElementById('filterMonth')?.value || '';
    const user = document.getElementById('filterUser')?.value || '';
    if (month) url.searchParams.set('filterMonth', month); else url.searchParams.delete('filterMonth');
    if (user) url.searchParams.set('filterUser', user); else url.searchParams.delete('filterUser');
    if (currentView !== 'kanban') url.searchParams.set('view', currentView); else url.searchParams.delete('view');
    window.history.replaceState(null, '', `${url.pathname}${url.search}${url.hash}`);
}

function restoreBoardFilterState(month, user) {
    const parameters = new URLSearchParams(window.location.search);
    const setIfAvailable = (element, value) => {
        if (value && [...element.options].some(option => option.value === value)) element.value = value;
    };
    if (month) setIfAvailable(month, parameters.get('filterMonth'));
    if (user) setIfAvailable(user, parameters.get('filterUser'));
}

let ganttAllTasks = [];
let ganttMode = 'Week';
let ganttResizeTimer;
let ganttInitialized = false;

async function persistGanttDates(task, start, end) {
    const original = ganttAllTasks.find(item => item.id === task.id);
    const startDate = ganttDateValue(start);
    const dueDate = ganttDateValue(end);
    if (!original || !startDate || !dueDate) return;
    try {
        const result = await postForm('/Boards/UpdateTaskSchedule', {
            taskId: task.id,
            expectedVersion: original.rowVersion,
            startDate,
            dueDate
        });
        if (!result.success) throw new Error(result.message || 'Não foi possível reagendar a tarefa.');
        original.start = startDate;
        original.end = dueDate;
        original.rowVersion += 1;
    } catch (error) {
        alert(error.message || 'Não foi possível reagendar a tarefa.');
        renderGantt(true);
    }
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
            if (!validateOperationForm(form)) return;

            const button = form.querySelector('button[type="submit"]');
            const originalButtonText = button?.textContent;
            if (button) {
                button.disabled = true;
                if (form.id === 'commentForm') button.textContent = 'Enviando...';
            }
            try {
                const response = await fetch(form.action, { method: 'POST', body: formDataWithAntiforgery(form) });
                const result = await jsonResponse(response);
                if (!response.ok || !result.success) {
                    throw new Error(result.message || 'Operação não concluída.');
                }
                rememberTaskDetails(form, result.message);
                window.location.reload();
            } catch (error) {
                showFormError(form, error.message || 'Erro de comunicação.');
                if (button) {
                    button.disabled = false;
                    button.textContent = originalButtonText;
                }
            }
        });
    });
}

function validateOperationForm(form) {
    if (form.id === 'checklistForm') {
        const title = form.elements.namedItem('title')?.value.trim() || '';
        if (!title || title.length > 300) {
            showFormError(form, 'Informe um item de até 300 caracteres.');
            return false;
        }
        return true;
    }

    if (form.id === 'timeLogForm') {
        const hours = Number.parseInt(form.elements.namedItem('hours')?.value || '0', 10) || 0;
        const minutes = Number.parseInt(form.elements.namedItem('minutes')?.value || '0', 10) || 0;
        if (hours < 0 || minutes < 0 || minutes > 59 || hours * 60 + minutes <= 0) {
            showFormError(form, 'Informe pelo menos um minuto e use no máximo 59 minutos no segundo campo.');
            return false;
        }
        const isBillable = form.elements.namedItem('isBillable')?.checked;
        const description = form.elements.namedItem('description')?.value.trim() || '';
        if (isBillable && !description) {
            showFormError(form, 'Descreva o trabalho realizado antes de enviar uma hora para aprovação de faturamento.');
            return false;
        }
        return true;
    }

    if (form.id === 'subtaskForm') {
        const title = form.elements.namedItem('title')?.value.trim() || '';
        if (!title || title.length > 200) {
            showFormError(form, 'Informe o título da subtarefa com até 200 caracteres.');
            return false;
        }
        return true;
    }

    if (form.id === 'taskFileForm') {
        const file = form.elements.namedItem('file')?.files?.[0];
        if (!file) {
            showFormError(form, 'Selecione um arquivo para enviar.');
            return false;
        }
        if (file.size <= 0 || file.size > 25 * 1024 * 1024) {
            showFormError(form, 'O arquivo deve ter até 25 MB.');
            return false;
        }
        return true;
    }

    if (form.id === 'commentForm') {
        const content = form.elements.namedItem('content')?.value.trim();
        const images = [...(form.elements.namedItem('images')?.files || [])];
        if (!content && images.length === 0) {
            showFormError(form, 'Escreva uma mensagem ou anexe pelo menos uma imagem.');
            form.elements.namedItem('content')?.focus();
            return false;
        }
        if (images.length > 4) {
            showFormError(form, 'Selecione no máximo 4 imagens.');
            return false;
        }
        const oversized = images.find(image => image.size > 8 * 1024 * 1024);
        if (oversized) {
            showFormError(form, `A imagem “${oversized.name}” ultrapassa 8 MB.`);
            return false;
        }
        return true;
    }

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
    if (form.id === 'commentForm') return document.getElementById('commentFormError');
    return form.querySelector('[data-form-error]');
}

function clearFormError(form) {
    const errorElement = formErrorElement(form);
    if (!errorElement) return;
    errorElement.textContent = '';
    errorElement.classList.add('hidden');
}

function showFormError(form, message) {
    let errorElement = formErrorElement(form);
    if (!errorElement) {
        if (form.id === 'deleteTaskForm') {
            alert(message);
            return;
        }
        errorElement = document.createElement('div');
        errorElement.dataset.formError = 'true';
        errorElement.setAttribute('role', 'alert');
        errorElement.className = 'rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700';
        form.prepend(errorElement);
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

function initHandoffValidation() {
    const requiresAcceptance = document.getElementById('HandoffRequiresAcceptance');
    requiresAcceptance?.addEventListener('change', syncHandoffValidation);
    syncHandoffValidation();
}

function syncHandoffValidation() {
    const requiresAcceptance = document.getElementById('HandoffRequiresAcceptance');
    const acceptanceConfig = document.getElementById('HandoffAcceptanceConfig');
    const acceptanceBy = document.getElementById('HandoffAcceptanceBy');
    const enabled = requiresAcceptance?.checked === true;

    acceptanceConfig?.classList.toggle('hidden', !enabled);
    if (acceptanceBy) acceptanceBy.disabled = !enabled;
}

function initTaskDetailTabs() {
    document.querySelectorAll('[data-task-tab]').forEach(button => {
        button.addEventListener('click', () => selectTaskDetailsTab(button.dataset.taskTab));
    });
}

function selectTaskDetailsTab(tabName = 'summary') {
    document.querySelectorAll('[data-task-panel]').forEach(panel =>
        panel.classList.toggle('hidden', panel.dataset.taskPanel !== tabName));
    document.querySelectorAll('[data-task-tab]').forEach(button => {
        const active = button.dataset.taskTab === tabName;
        button.classList.toggle('bg-white', active);
        button.classList.toggle('shadow-sm', active);
        button.classList.toggle('text-indigo-700', active);
        button.classList.toggle('text-slate-600', !active);
        button.setAttribute('aria-selected', active ? 'true' : 'false');
    });
}

function rememberTaskDetails(form, serverMessage) {
    const activityForms = ['commentForm', 'checklistForm', 'timeLogForm', 'subtaskForm', 'taskFileForm'];
    const workflowForms = ['dependencyForm', 'returnQuestionForm', 'handoffTaskForm'];
    const taskId = form.elements.namedItem('taskId')?.value || form.elements.namedItem('parentTaskId')?.value;
    if (!taskId || (!activityForms.includes(form.id) && !workflowForms.includes(form.id) && form.id !== 'editTaskForm')) return;
    sessionStorage.setItem('boards.reopenTask', JSON.stringify({
        taskId,
        tab: activityForms.includes(form.id) ? 'activity' : workflowForms.includes(form.id) ? 'workflow' : 'summary',
        status: serverMessage || (form.id === 'commentForm' ? 'Mensagem enviada e registrada no histórico.' : '')
    }));
}

function restoreTaskDetailsAfterReload() {
    const raw = sessionStorage.getItem('boards.reopenTask');
    if (!raw) return false;
    sessionStorage.removeItem('boards.reopenTask');
    try {
        const state = JSON.parse(raw);
        const taskElement = [...document.querySelectorAll('[data-task-id]')]
            .find(element => element.dataset.taskId === state.taskId && element.dataset.title !== undefined);
        if (!taskElement) return false;
        openTaskDetailsModal(taskElement);
        selectTaskDetailsTab(state.tab || 'summary');
        if (state.status) {
            const status = document.getElementById('commentFormStatus');
            if (status) {
                status.textContent = state.status;
                status.classList.remove('hidden');
                status.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
        }
        return true;
    } catch {
        sessionStorage.removeItem('boards.reopenTask');
        return false;
    }
}

function openTaskDetailsFromUrl() {
    const parameters = new URLSearchParams(window.location.search);
    const taskId = parameters.get('taskId');
    if (!taskId) return false;
    const taskElement = [...document.querySelectorAll('[data-task-id]')]
        .find(element => element.dataset.taskId === taskId && element.dataset.title !== undefined);
    if (!taskElement) return false;
    openTaskDetailsModal(taskElement);
    const requestedTab = parameters.get('tab');
    selectTaskDetailsTab(['summary', 'activity', 'workflow'].includes(requestedTab) ? requestedTab : 'summary');
    return true;
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
    restoreBoardFilterState(month, user);
    const matchesFilters = item => {
        const monthOk = !month?.value ||
            (month.value === 'inbox' ? !item.dataset.targetMonth : item.dataset.targetMonth === month.value);
        const userOk = !user?.value || item.dataset.assignedTo === user.value ||
            (item.dataset.collaborators || '').split(',').includes(user.value);
        return monthOk && userOk;
    };
    const apply = () => {
        document.querySelectorAll('.kanban-task, .task-filter-row').forEach(item => {
            item.classList.toggle('hidden', !matchesFilters(item));
        });
        const tasks = [...document.querySelectorAll('[data-task-metric]')].filter(matchesFilters);
        const setText = (id, value) => { const element = document.getElementById(id); if (element) element.textContent = value; };
        setText('filteredTotal', String(tasks.length));
        setText('filteredDone', String(tasks.filter(item => item.dataset.status === 'done').length));
        setText('filteredBlocked', String(tasks.filter(item => item.dataset.isBlocked === 'true').length));
        const minutes = tasks.reduce((total, item) => total + (Number.parseInt(item.dataset.loggedMinutes || '0', 10) || 0), 0);
        setText('filteredHours', `${(minutes / 60).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}h`);
        document.querySelectorAll('[data-column-count]').forEach(counter => {
            counter.textContent = String(tasks.filter(item => item.dataset.status === counter.dataset.columnCount).length);
        });
    };
    const applyAndPersist = () => {
        apply();
        refreshVisibleGantt();
        persistBoardViewState();
    };
    month?.addEventListener('change', applyAndPersist);
    user?.addEventListener('change', applyAndPersist);
    apply();
}

async function postForm(url, values) {
    const body = new URLSearchParams({ ...values, __RequestVerificationToken: antiforgeryToken() });
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body
    });
    const result = await jsonResponse(response);
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
    const canEditTask = get('canEdit') === 'true';
    const editForm = document.getElementById('editTaskForm');
    editForm.reset();
    clearFormError(editForm);
    editForm.querySelectorAll('input:not([type="hidden"]), textarea, select').forEach(input => {
        input.disabled = !canEditTask;
    });
    editForm.querySelector('button[type="submit"]')?.classList.toggle('hidden', !canEditTask);
    const handoffForm = document.getElementById('handoffTaskForm');
    handoffForm?.reset();
    syncHandoffValidation();
    if (handoffForm) clearFormError(handoffForm);
    ['dependencyForm', 'returnQuestionForm', 'checklistForm', 'timeLogForm', 'subtaskForm', 'taskFileForm']
        .forEach(formId => {
            const operationForm = document.getElementById(formId);
            operationForm?.reset();
            if (operationForm) clearFormError(operationForm);
        });
    const commentForm = document.getElementById('commentForm');
    commentForm?.reset();
    if (commentForm) clearFormError(commentForm);
    document.getElementById('commentFormStatus')?.classList.add('hidden');
    const detailsTitle = document.getElementById('taskDetailsTitle');
    if (detailsTitle) detailsTitle.textContent = get('title') || 'Consulte o planejamento, converse e registre a execução.';
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
    const slaInput = document.getElementById('EditSlaMinutes');
    if (slaInput) slaInput.value = get('slaMinutes');
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
    const replyTo = document.getElementById('CommentReplyTo');
    if (replyTo) replyTo.value = '';
    document.querySelectorAll('.comment-reply-option').forEach(option => {
        const visible = option.dataset.taskId === taskId;
        option.disabled = !visible;
        option.hidden = !visible;
    });
    const mentionableUsers = new Set([
        get('assignedTo'), get('accountableOwnerId'), get('createdBy'), get('boardOwnerId'),
        ...get('collaborators').split(',')
    ].filter(userId => userId && userId !== get('currentUserId')));
    let visibleMentionCount = 0;
    document.querySelectorAll('.comment-mention-option').forEach(label => {
        const visible = mentionableUsers.has(label.dataset.userId);
        label.classList.toggle('hidden', !visible);
        label.querySelector('input').disabled = !visible;
        if (visible) visibleMentionCount += 1;
    });
    document.getElementById('commentMentionEmpty')?.classList.toggle('hidden', visibleMentionCount > 0);
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
    selectTaskDetailsTab('summary');
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

function confirmTaskDeletion({ title, permanentlyDelete }) {
    const modal = document.getElementById('taskDeletionConfirmModal');
    const heading = document.getElementById('taskDeletionConfirmTitle');
    const description = document.getElementById('taskDeletionConfirmDescription');
    const notice = document.getElementById('taskDeletionConfirmNotice');
    const cancel = document.getElementById('taskDeletionConfirmCancel');
    const action = document.getElementById('taskDeletionConfirmAction');

    if (!modal || !heading || !description || !notice || !cancel || !action)
        return Promise.resolve(false);

    const taskTitle = title || 'esta tarefa';
    heading.textContent = permanentlyDelete ? 'Excluir tarefa definitivamente?' : 'Arquivar tarefa?';
    description.textContent = permanentlyDelete
        ? `Você está prestes a excluir “${taskTitle}”.`
        : `“${taskTitle}” sairá do fluxo ativo, mas continuará disponível nas tarefas arquivadas.`;
    notice.textContent = permanentlyDelete
        ? 'A tarefa será arquivada primeiro. Se houver horas apontadas, subtarefas ou vínculos que exigem histórico, ela permanecerá arquivada para proteger os dados.'
        : 'Conversas, horas, arquivos e histórico serão preservados. Um gestor poderá restaurar a tarefa quando necessário.';
    action.textContent = permanentlyDelete ? 'Excluir definitivamente' : 'Arquivar tarefa';
    action.className = permanentlyDelete
        ? 'rounded-lg bg-red-600 px-4 py-2.5 text-sm font-bold text-white shadow-sm hover:bg-red-700 focus:outline-none focus:ring-4 focus:ring-red-100'
        : 'rounded-lg bg-amber-600 px-4 py-2.5 text-sm font-bold text-white shadow-sm hover:bg-amber-700 focus:outline-none focus:ring-4 focus:ring-amber-100';

    return new Promise(resolve => {
        const close = confirmed => {
            modal.classList.add('hidden');
            modal.classList.remove('flex');
            cancel.removeEventListener('click', cancelAction);
            action.removeEventListener('click', confirmAction);
            modal.removeEventListener('click', onBackdropClick);
            document.removeEventListener('keydown', onKeyDown);
            resolve(confirmed);
        };
        const cancelAction = () => close(false);
        const confirmAction = () => close(true);
        const onBackdropClick = event => { if (event.target === modal) close(false); };
        const onKeyDown = event => { if (event.key === 'Escape') close(false); };

        cancel.addEventListener('click', cancelAction);
        action.addEventListener('click', confirmAction);
        modal.addEventListener('click', onBackdropClick);
        document.addEventListener('keydown', onKeyDown);
        modal.classList.remove('hidden');
        modal.classList.add('flex');
        lucide?.createIcons();
        action.focus();
    });
}

window.confirmDeleteTask = async () => {
    const title = document.getElementById('EditTitle')?.value?.trim() || 'esta tarefa';
    if (!await confirmTaskDeletion({ title, permanentlyDelete: false })) return;
    document.getElementById('DeleteTaskId').value = document.getElementById('EditTaskId').value;
    document.getElementById('deleteTaskForm').requestSubmit();
};

window.confirmPermanentlyDeleteCurrentTask = async () => {
    const taskId = document.getElementById('EditTaskId')?.value;
    const title = document.getElementById('EditTitle')?.value?.trim() || 'esta tarefa';
    if (!taskId) return;
    if (!await confirmTaskDeletion({ title, permanentlyDelete: true })) return;

    try {
        const result = await postForm('/Boards/ArchiveAndPermanentlyDeleteTask', { taskId });
        if (result.success) {
            window.location.reload();
            return;
        }
        if (result.archived) {
            alert(`${result.message || 'A exclusão definitiva não foi permitida.'}\n\nA tarefa foi arquivada e pode ser restaurada na seção “Tarefas arquivadas”.`);
            window.location.reload();
            return;
        }
        throw new Error(result.message || 'Não foi possível excluir a tarefa.');
    } catch (error) {
        alert(error.message || 'Não foi possível excluir a tarefa.');
    }
};

window.restoreTask = async taskId => {
    try {
        const result = await postForm('/Boards/RestoreTask', { taskId });
        if (!result.success) throw new Error(result.message || 'Não foi possível restaurar a tarefa.');
        window.location.reload();
    } catch (error) { alert(error.message || 'Não foi possível restaurar a tarefa.'); }
};

window.permanentlyDeleteTask = async (taskId, title) => {
    if (!await confirmTaskDeletion({ title, permanentlyDelete: true })) return;
    try {
        const result = await postForm('/Boards/PermanentlyDeleteTask', { taskId });
        if (!result.success) throw new Error(result.message || 'Não foi possível excluir a tarefa definitivamente.');
        window.location.reload();
    } catch (error) {
        alert(error.message || 'Não foi possível excluir a tarefa definitivamente.');
    }
};

window.toggleChecklist = async (id, completed) => {
    try {
        const result = await postForm('/Boards/ToggleChecklistItem', { id, completed });
        if (!result.success) throw new Error(result.message || 'Não foi possível atualizar o item.');
    } catch (error) {
        alert(error.message || 'Não foi possível atualizar o item.');
    }
};

window.deleteChecklist = async id => {
    if (!confirm('Excluir este item?')) return;
    try {
        const result = await postForm('/Boards/DeleteChecklistItem', { id });
        if (!result.success) throw new Error(result.message || 'Não foi possível excluir o item.');
        window.location.reload();
    } catch (error) {
        alert(error.message || 'Não foi possível excluir o item.');
    }
};

window.editComment = async (commentId, currentContent) => {
    const content = prompt('Editar comentário:', currentContent);
    if (!content?.trim()) return;
    try {
        const result = await postForm('/Boards/UpdateComment', { commentId, content });
        if (!result.success) throw new Error(result.message || 'Não foi possível editar o comentário.');
        window.location.reload();
    } catch (error) {
        alert(error.message || 'Não foi possível editar o comentário.');
    }
};

window.deleteComment = async commentId => {
    if (!confirm('Excluir este comentário?')) return;
    try {
        const result = await postForm('/Boards/DeleteComment', { commentId });
        if (!result.success) throw new Error(result.message || 'Não foi possível excluir o comentário.');
        window.location.reload();
    } catch (error) {
        alert(error.message || 'Não foi possível excluir o comentário.');
    }
};

window.deleteDependency = async dependencyId => {
    if (!confirm('Remover este pré-requisito?')) return;
    try {
        const result = await postForm('/Work/DeleteDependency', { dependencyId });
        if (!result.success) throw new Error(result.message || 'Não foi possível remover o pré-requisito.');
        window.location.reload();
    } catch (error) {
        alert(error.message || 'Não foi possível remover o pré-requisito.');
    }
};
