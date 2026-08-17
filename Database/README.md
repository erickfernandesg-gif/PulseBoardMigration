# Banco de dados do PulseBoard

O projeto Supabase `cvusdlvkgltvwmqyadeg` já recebeu estas migrações. Os arquivos permanecem no repositório para auditoria e recuperação.

Para uma instalação nova, execute na ordem:

1. `pulseboard_schema.sql`
2. `install_task_chat.sql`
3. `enterprise_upgrade.sql`
4. `security_hardening.sql`
5. `fix_tasks_rls_recursion.sql`
6. `boards_reliability_upgrade.sql`
7. `notification_compatibility_fix.sql`
8. `respond_assignment_notification_fix.sql`
9. `task_assignment_trigger_fix.sql`
10. `board_operations_suite.sql` — colunas/WIP, ações em massa, intake, auditoria por campo, aprovações, espelhos, dependências e SLA.
11. `board_operations_integration_fix.sql` — validação e integração de aprovações, substitutos, automações e ciclos de dependência.
12. `import_monday_cronograma_2026.sql` e `finalize_monday_cronograma_2026.sql` — carga idempotente do cronograma corporativo exportado do Monday.

Os scripts de upgrade são idempotentes sempre que possível. Alterações de produção devem ser aplicadas como migrações, nunca colando somente trechos isolados sem testar em uma transação.

## Verificações obrigatórias

- Usuário comum visualiza apenas Boards em que participa.
- Executor consegue aceitar, concluir, devolver e transferir uma atribuição.
- Uma tarefa não conclui com dependência ou checklist pendente.
- Arquivar preserva horas, comentários, arquivos e histórico.
- Edição com `row_version` antiga é recusada.
- Toda atribuição gera registro em `task_assignments` e alerta para o destinatário.
