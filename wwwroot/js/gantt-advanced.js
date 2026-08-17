let ganttSmartFilter = 'all';
let ganttDependenciesVisible = true;
let ganttListVisible = true;

function initGanttControls() {
    const dataElement = document.getElementById('gantt-data');
    if (!dataElement) return;
    try {
        ganttAllTasks = JSON.parse(dataElement.textContent || '[]');
    } catch {
        showGanttError('Não foi possível interpretar os dados do cronograma.');
        return;
    }

    document.querySelectorAll('[data-gantt-mode]').forEach(button =>
        button.addEventListener('click', () => changeGanttMode(button.dataset.ganttMode)));
    document.querySelectorAll('[data-gantt-smart-filter]').forEach(button => {
        button.addEventListener('click', () => {
            ganttSmartFilter = button.dataset.ganttSmartFilter || 'all';
            document.querySelectorAll('[data-gantt-smart-filter]').forEach(item =>
                item.classList.toggle('is-active', item === button));
            renderGantt();
        });
    });
    ['ganttStatusFilter', 'ganttSort', 'ganttShowSubtasks'].forEach(id =>
        document.getElementById(id)?.addEventListener('change', () => renderGantt()));
    document.getElementById('ganttSearch')?.addEventListener('input', () => {
        clearTimeout(ganttResizeTimer);
        ganttResizeTimer = setTimeout(() => renderGantt(), 180);
    });
    document.getElementById('ganttToday')?.addEventListener('click', () =>
        window.ganttChartInstance?.scroll_current?.());
    document.getElementById('ganttToggleDependencies')?.addEventListener('click', event => {
        ganttDependenciesVisible = !ganttDependenciesVisible;
        event.currentTarget.setAttribute('aria-pressed', String(ganttDependenciesVisible));
        document.getElementById('ganttWorkspace')?.classList.toggle('dependencies-hidden', !ganttDependenciesVisible);
    });
    document.getElementById('ganttToggleList')?.addEventListener('click', event => {
        ganttListVisible = !ganttListVisible;
        event.currentTarget.setAttribute('aria-pressed', String(ganttListVisible));
        document.getElementById('gantt-stage')?.classList.toggle('list-hidden', !ganttListVisible);
        clearTimeout(ganttResizeTimer);
        ganttResizeTimer = setTimeout(() => renderGantt(true), 220);
    });
    document.getElementById('ganttFullscreen')?.addEventListener('click', async () => {
        const workspace = document.getElementById('ganttWorkspace');
        if (!workspace) return;
        if (document.fullscreenElement) await document.exitFullscreen();
        else await workspace.requestFullscreen();
    });
    document.addEventListener('fullscreenchange', () => {
        const icon = document.querySelector('#ganttFullscreen i');
        icon?.setAttribute('data-lucide', document.fullscreenElement ? 'minimize-2' : 'maximize-2');
        window.lucide?.createIcons();
        if (!document.getElementById('view-gantt')?.classList.contains('hidden')) {
            clearTimeout(ganttResizeTimer);
            ganttResizeTimer = setTimeout(() => renderGantt(true), 120);
        }
    });
    document.getElementById('gantt-scroll')?.addEventListener('scroll', syncGanttTaskList, { passive: true });
    window.addEventListener('resize', () => {
        if (document.getElementById('view-gantt')?.classList.contains('hidden')) return;
        clearTimeout(ganttResizeTimer);
        ganttResizeTimer = setTimeout(() => renderGantt(true), 180);
    });
    updateGanttIndicators(ganttAllTasks);
}

function ensureGantt() {
    if (document.getElementById('gantt-chart')) renderGantt(ganttInitialized);
}

function filteredGanttTasks() {
    const month = document.getElementById('filterMonth')?.value || '';
    const user = document.getElementById('filterUser')?.value || '';
    const search = normalizeGanttText(document.getElementById('ganttSearch')?.value || '');
    const status = document.getElementById('ganttStatusFilter')?.value || 'all';
    const showSubtasks = document.getElementById('ganttShowSubtasks')?.checked !== false;
    const baseTasks = ganttAllTasks.filter(task => {
        const collaborators = (task.collaborators || '').split(',').filter(Boolean);
        const haystack = normalizeGanttText(`${task.name} ${task.responsible} ${task.client}`);
        return (!month || (month === 'inbox' ? !task.targetMonth : task.targetMonth === month))
            && (!user || task.assignedTo === user || collaborators.includes(user))
            && (status === 'all' || (status === 'open' ? !task.isDone : task.status === status))
            && (showSubtasks || !task.isSubtask)
            && (!search || haystack.includes(search));
    });

    updateGanttIndicators(baseTasks);
    const smartTasks = baseTasks.filter(task => {
        if (ganttSmartFilter === 'risk') return task.isRisk;
        if (ganttSmartFilter === 'overdue') return task.isOverdue;
        if (ganttSmartFilter === 'done') return task.isDone;
        return true;
    });
    const sort = document.getElementById('ganttSort')?.value || 'start';
    return [...smartTasks].sort((left, right) => {
        if (sort === 'end') return left.end.localeCompare(right.end) || left.start.localeCompare(right.start);
        if (sort === 'responsible') return left.responsible.localeCompare(right.responsible, 'pt-BR') || left.start.localeCompare(right.start);
        if (sort === 'name') return left.name.localeCompare(right.name, 'pt-BR');
        return left.start.localeCompare(right.start) || left.end.localeCompare(right.end);
    });
}

function renderGantt(maintainPosition = false) {
    const chart = document.getElementById('gantt-chart');
    const scroll = document.getElementById('gantt-scroll');
    const empty = document.getElementById('ganttEmpty');
    const stage = document.getElementById('gantt-stage');
    if (!chart || !scroll || !empty || !stage) return;
    if (typeof window.Gantt !== 'function') {
        showGanttError('A biblioteca do Gantt não foi carregada. Verifique a conexão e atualize a página.');
        return;
    }

    const filteredTasks = filteredGanttTasks();
    const visibleIds = new Set(filteredTasks.map(task => task.id));
    const tasks = filteredTasks.map(task => ({
        ...task,
        dependencies: (task.dependencies || '').split(',').filter(id => visibleIds.has(id)).join(',')
    }));
    stage.classList.toggle('hidden', tasks.length === 0);
    empty.classList.toggle('hidden', tasks.length > 0);
    if (!tasks.length) {
        chart.replaceChildren();
        renderGanttTaskList([]);
        window.ganttChartInstance = null;
        ganttInitialized = false;
        return;
    }

    const previous = maintainPosition ? { left: scroll.scrollLeft, top: scroll.scrollTop } : null;
    chart.replaceChildren();
    try {
        window.ganttChartInstance = new Gantt('#gantt-chart', tasks, {
            header_height: 50,
            column_width: ganttMode === 'Day' ? 36 : ganttMode === 'Month' ? 124 : 44,
            bar_height: 22,
            bar_corner_radius: 6,
            padding: 16,
            step: 24,
            view_mode: ganttMode,
            language: 'ptBr',
            on_click: task => openTaskFromGantt(task.id),
            on_date_change: (task, start, end) => persistGanttDates(task, start, end),
            custom_popup_html: ganttPopupHtml
        });
        renderGanttTaskList(tasks);
        updateGanttRange(tasks);
        ganttInitialized = true;
        hideGanttError();
        if (previous) {
            scroll.scrollLeft = previous.left;
            scroll.scrollTop = previous.top;
        } else {
            window.ganttChartInstance?.scroll_current?.();
        }
        syncGanttTaskList();
    } catch (error) {
        ganttInitialized = false;
        showGanttError(error?.message || 'Não foi possível montar o cronograma.');
    }
}

function ganttPopupHtml(task) {
    const variance = task.baselineVariance == null ? '' : task.baselineVariance > 0
        ? `<div class="mt-2 text-[10px] font-semibold text-red-600">${task.baselineVariance} dia(s) após a linha de base</div>`
        : '<div class="mt-2 text-[10px] font-semibold text-emerald-600">Dentro da linha de base</div>';
    return `<div class="gantt-popup-card">
        <div class="mb-1 text-sm font-extrabold text-slate-900">${escapeHtml(task.name)}</div>
        <div class="mb-3 flex flex-wrap gap-1.5">
            <span class="rounded-full bg-slate-100 px-2 py-1 text-[9px] font-bold uppercase text-slate-600">${escapeHtml(task.statusLabel || task.status)}</span>
            ${task.isBlocked ? '<span class="rounded-full bg-red-50 px-2 py-1 text-[9px] font-bold uppercase text-red-600">Bloqueada</span>' : ''}
            ${task.isOverdue ? '<span class="rounded-full bg-amber-50 px-2 py-1 text-[9px] font-bold uppercase text-amber-700">Atrasada</span>' : ''}
        </div>
        <div class="grid grid-cols-2 gap-x-4 gap-y-2">
            <div><span class="block text-[9px] font-bold uppercase text-slate-400">Responsável</span>${escapeHtml(task.responsible)}</div>
            <div><span class="block text-[9px] font-bold uppercase text-slate-400">Cliente</span>${escapeHtml(task.client)}</div>
            <div class="col-span-2"><span class="block text-[9px] font-bold uppercase text-slate-400">Período</span>${formatGanttDate(task.start)} → ${formatGanttDate(task.end)} · ${task.durationDays} dia(s)</div>
        </div>
        <div class="mt-3 flex items-center justify-between"><span class="font-bold">Progresso</span><strong class="text-indigo-600">${Math.round(task.progress || 0)}%</strong></div>
        <div class="gantt-popup-progress mt-1"><i style="width:${Math.max(0, Math.min(100, task.progress || 0))}%"></i></div>
        ${variance}<div class="mt-3 border-t border-slate-100 pt-2 text-[10px] font-bold text-indigo-600">Clique na barra para abrir a tarefa</div>
    </div>`;
}

function changeGanttMode(mode) {
    if (!['Day', 'Week', 'Month'].includes(mode)) return;
    ganttMode = mode;
    document.querySelectorAll('[data-gantt-mode]').forEach(button =>
        button.classList.toggle('is-active', button.dataset.ganttMode === mode));
    renderGantt(true);
}

function normalizeGanttText(value) {
    return String(value || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}

function updateGanttIndicators(tasks) {
    const values = {
        ganttVisibleCount: tasks.length,
        ganttRiskCount: tasks.filter(task => task.isRisk).length,
        ganttOverdueCount: tasks.filter(task => task.isOverdue).length,
        ganttDoneCount: tasks.filter(task => task.isDone).length
    };
    Object.entries(values).forEach(([id, value]) => {
        const element = document.getElementById(id);
        if (element) element.textContent = String(value);
    });
}

function renderGanttTaskList(tasks) {
    const list = document.getElementById('gantt-task-list');
    if (!list) return;
    list.replaceChildren();
    tasks.forEach(task => {
        const row = document.createElement('button');
        row.type = 'button';
        row.className = 'gantt-task-row w-full text-left';
        row.dataset.taskId = task.id;
        row.title = `${task.name} — ${task.responsible}`;
        row.innerHTML = `<span class="min-w-0"><span class="flex items-center gap-1.5"><i class="gantt-risk-dot ${task.isRisk ? 'bg-red-500' : task.isDone ? 'bg-emerald-500' : task.priority === 'high' ? 'bg-amber-500' : 'bg-indigo-500'}"></i><span class="gantt-task-title">${escapeHtml(task.name)}</span></span><span class="gantt-task-meta block pl-3">${escapeHtml(task.responsible)} · ${task.progress}%</span></span><span class="gantt-task-due${task.isOverdue ? ' is-overdue' : ''}">${formatGanttDate(task.end).slice(0, 5)}</span>`;
        row.addEventListener('click', () => openTaskFromGantt(task.id));
        row.addEventListener('mouseenter', () => highlightGanttTask(task.id, true));
        row.addEventListener('mouseleave', () => highlightGanttTask(task.id, false));
        list.append(row);
    });
}

function syncGanttTaskList() {
    const scroll = document.getElementById('gantt-scroll');
    const list = document.getElementById('gantt-task-list');
    if (scroll && list) list.style.transform = `translateY(-${scroll.scrollTop}px)`;
}

function highlightGanttTask(taskId, active) {
    const safeId = window.CSS?.escape ? CSS.escape(taskId) : taskId;
    document.querySelector(`#gantt-chart .bar-wrapper[data-id="${safeId}"]`)?.classList.toggle('active', active);
}

function openTaskFromGantt(taskId) {
    const safeId = window.CSS?.escape ? CSS.escape(taskId) : taskId;
    const card = document.querySelector(`.kanban-task[data-task-id="${safeId}"]`);
    if (card) openTaskDetailsModal(card);
}

function updateGanttRange(tasks) {
    const label = document.getElementById('ganttRangeLabel');
    if (!label || !tasks.length) return;
    const start = tasks.reduce((value, task) => task.start < value ? task.start : value, tasks[0].start);
    const end = tasks.reduce((value, task) => task.end > value ? task.end : value, tasks[0].end);
    label.textContent = `${formatGanttDate(start)} → ${formatGanttDate(end)} · ${tasks.length} atividade(s)`;
}
