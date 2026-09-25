document.addEventListener('DOMContentLoaded', () => {
    const project = document.getElementById('scheduleProjectFilter');
    const person = document.getElementById('schedulePersonFilter');
    const risk = document.getElementById('scheduleRiskFilter');
    const clear = document.getElementById('clearScheduleFilters');
    const empty = document.getElementById('scheduleEmptyState');
    const count = document.getElementById('visibleScheduleCount');
    const projectCount = document.getElementById('visibleProjectCount');
    const rows = [...document.querySelectorAll('[data-schedule-row]')];
    const projects = [...document.querySelectorAll('[data-schedule-project]')];

    const matchesRisk = row => {
        const value = risk?.value || '';
        if (!value) return true;
        const overdue = row.dataset.overdue === 'true';
        const blocked = row.dataset.blocked === 'true';
        const critical = row.dataset.critical === 'true';
        return value === 'attention' ? overdue || blocked || critical
            : value === 'overdue' ? overdue
            : value === 'blocked' ? blocked
            : critical;
    };

    const apply = () => {
        const visibleRows = rows.filter(row => {
            const projectMatches = !project?.value || row.dataset.projectId === project.value;
            const personMatches = !person?.value || row.dataset.assigneeId === person.value;
            return projectMatches && personMatches && matchesRisk(row);
        });
        const visibleSet = new Set(visibleRows);
        rows.forEach(row => row.classList.toggle('hidden', !visibleSet.has(row)));

        let visibleProjects = 0;
        projects.forEach(group => {
            const hasVisibleTask = [...group.querySelectorAll('[data-schedule-row]')].some(row => visibleSet.has(row));
            group.classList.toggle('hidden', !hasVisibleTask);
            if (hasVisibleTask) visibleProjects += 1;
        });

        if (count) count.textContent = String(visibleRows.length);
        if (projectCount) projectCount.textContent = String(visibleProjects);
        empty?.classList.toggle('hidden', visibleRows.length !== 0);
    };

    [project, person, risk].forEach(filter => filter?.addEventListener('change', apply));
    clear?.addEventListener('click', () => {
        if (project) project.value = '';
        if (person) person.value = '';
        if (risk) risk.value = '';
        apply();
    });
});
