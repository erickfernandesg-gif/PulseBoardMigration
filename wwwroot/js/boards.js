document.addEventListener('DOMContentLoaded', () => {
    // 1. Inicializar funcionalidades assim que o DOM estiver pronto
    initTabs();
    initDragAndDrop();
    initAjaxForms();
});

// ==========================================
// 1. ALTERNÂNCIA DE VIEWS (TABS)
// ==========================================
function initTabs() {
    const tabButtons = document.querySelectorAll('[data-view-target]');

    tabButtons.forEach(button => {
        button.addEventListener('click', (e) => {
            // Qual é a view que queremos abrir? (ex: 'kanban', 'table', 'gantt')
            const targetView = button.getAttribute('data-view-target');

            // Esconde todas as views
            document.getElementById('view-kanban')?.classList.add('hidden');
            document.getElementById('view-table')?.classList.add('hidden');
            document.getElementById('view-gantt')?.classList.add('hidden');

            // Mostra a view correta
            document.getElementById(`view-${targetView}`)?.classList.remove('hidden');

            // Opcional: Atualizar o estilo dos botões para mostrar qual está ativo
            tabButtons.forEach(btn => btn.classList.remove('bg-indigo-50', 'text-indigo-600'));
            button.classList.add('bg-indigo-50', 'text-indigo-600');
        });
    });
}

// ==========================================
// 2. DRAG AND DROP (SortableJS)
// ==========================================
function initDragAndDrop() {
    // Seleciona todas as áreas onde os cartões podem ser largados
    const columns = document.querySelectorAll('.kanban-column-body');

    if (columns.length === 0) return; // Prevenção caso não esteja na view de Kanban

    columns.forEach(col => {
        new Sortable(col, {
            group: 'kanban-board', // Permite arrastar entre colunas com o mesmo grupo
            animation: 150,
            ghostClass: 'bg-slate-100', // Classe CSS aplicada ao espaço vazio enquanto arrasta

            // Evento disparado quando o utilizador larga o cartão numa coluna
            onEnd: async function (evt) {
                const taskElement = evt.item;
                const taskId = taskElement.getAttribute('data-task-id');
                const newColumnId = evt.to.getAttribute('data-column-id');

                // Se largar na mesma coluna, não faz nada
                if (evt.from === evt.to) return;

                try {
                    // Chama o nosso endpoint AJAX no Controller
                    const response = await fetch('/Boards/MoveTask', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/x-www-form-urlencoded',
                        },
                        // Serializa os dados para Form UrlEncoded
                        body: new URLSearchParams({
                            taskId: taskId,
                            newColumnId: newColumnId
                        })
                    });

                    const result = await response.json();

                    if (!result.success) {
                        console.error('Erro ao mover a tarefa no banco:', result.message);
                        // Se der erro, cancela a ação e volta o card para a coluna original
                        evt.from.appendChild(taskElement);
                    }
                } catch (error) {
                    console.error('Erro de rede ao mover a tarefa', error);
                    evt.from.appendChild(taskElement);
                }
            },
        });
    });
}

// ==========================================
// 3. INTERCEPTAÇÃO DOS MODAIS (AJAX)
// ==========================================
function initAjaxForms() {
    const createForm = document.getElementById('createTaskForm');
    const editForm = document.getElementById('editTaskForm');
    const deleteForm = document.getElementById('deleteTaskForm');

    // Função genérica para tratar os 3 formulários
    const handleFormSubmit = async (e, form, loadingText) => {
        e.preventDefault();

        const submitBtn = form.querySelector('button[type="submit"]');
        let originalText = '';

        // Se houver botão (o form de exclusão não tem botão de submit visível), muda o estado
        if (submitBtn) {
            originalText = submitBtn.innerHTML;
            submitBtn.innerHTML = `<i data-lucide="loader-2" class="h-4 w-4 animate-spin"></i> ${loadingText}...`;
            submitBtn.disabled = true;
        }

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form)
            });

            const result = await response.json();

            if (result.success) {
                // Recarrega a página suavemente para atualizar Kanban, Gantt e Tabela com os novos dados
                window.location.reload();
            } else {
                alert('Erro: ' + result.message);
            }
        } catch (error) {
            console.error('Erro na requisição:', error);
            alert('Erro de conexão ao servidor.');
        } finally {
            if (submitBtn) {
                submitBtn.innerHTML = originalText;
                submitBtn.disabled = false;
                if (window.lucide) lucide.createIcons();
            }
        }
    };

    if (createForm) createForm.addEventListener('submit', (e) => handleFormSubmit(e, createForm, 'A criar'));
    if (editForm) editForm.addEventListener('submit', (e) => handleFormSubmit(e, editForm, 'A guardar'));
    if (deleteForm) deleteForm.addEventListener('submit', (e) => handleFormSubmit(e, deleteForm, 'A excluir'));
}

// ==========================================
// FUNÇÕES GLOBAIS DE MODAIS
// ==========================================

// Expõe as funções para o escopo global (para o onclick do HTML funcionar)
window.openCreateTaskModal = function (columnId = 'todo') {
    const modal = document.getElementById('createTaskModal');
    const select = document.getElementById('CreateColumnId');
    if (select) select.value = columnId;
    modal.classList.remove('hidden');
};

window.closeCreateTaskModal = function () {
    document.getElementById('createTaskModal').classList.add('hidden');
    document.getElementById('createTaskForm').reset();
};

window.openTaskDetailsModal = function (element) {
    const modal = document.getElementById('taskDetailsModal');

    // 1. Dados Básicos
    document.getElementById('EditTaskId').value = element.getAttribute('data-task-id') || '';
    document.getElementById('EditTitle').value = element.getAttribute('data-title') || '';
    document.getElementById('EditDescription').value = element.getAttribute('data-description') || '';

    // 2. Status e Responsável
    const columnId = element.getAttribute('data-status');
    const columnEl = document.getElementById('EditColumnId');
    if (columnId && columnEl) columnEl.value = columnId;

    const assigneeId = element.getAttribute('data-assignee-id');
    const assigneeEl = document.getElementById('EditAssigneeId');
    if (assigneeEl) assigneeEl.value = assigneeId || '';

    // 3. Prioridade
    const priority = element.getAttribute('data-priority');
    if (priority) document.getElementById('EditPriority').value = priority;

    // 4. Datas
    const startDate = element.getAttribute('data-start-date');
    const startEl = document.getElementById('EditStartDate');
    if (startEl) startEl.value = startDate || '';

    const dueDate = element.getAttribute('data-due');
    const dueEl = document.getElementById('EditDueDate');
    if (dueEl) dueEl.value = dueDate || '';

    // 5. Novos Campos Avançados
    const dept = element.getAttribute('data-department');
    const deptEl = document.getElementById('EditDepartment');
    if (deptEl) deptEl.value = dept || '';

    const risk = element.getAttribute('data-risk-level');
    const riskEl = document.getElementById('EditRiskLevel');
    if (riskEl) riskEl.value = risk || 'Nenhum'; // Fallback padrão

    const points = element.getAttribute('data-story-points');
    const pointsEl = document.getElementById('EditStoryPoints');
    if (pointsEl) pointsEl.value = points || '';

    const tags = element.getAttribute('data-tags');
    const tagsEl = document.getElementById('EditTags');
    if (tagsEl) tagsEl.value = tags || '';

    modal.classList.remove('hidden');
};

window.closeTaskDetailsModal = function () {
    document.getElementById('taskDetailsModal').classList.add('hidden');
};

window.confirmDeleteTask = function () {
    if (confirm("Tem certeza que deseja excluir esta tarefa? Esta ação não pode ser desfeita.")) {
        const taskId = document.getElementById('EditTaskId').value;
        document.getElementById('DeleteTaskId').value = taskId;

        // Dispara o formulário de deleção
        const deleteForm = document.getElementById('deleteTaskForm');
        if (deleteForm) {
            deleteForm.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
        }
    }
};