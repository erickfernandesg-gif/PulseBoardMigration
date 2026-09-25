# Banco de dados do PulseBoard

O projeto Supabase `cvusdlvkgltvwmqyadeg` já recebeu estas migrações. Os arquivos permanecem no repositório para auditoria e recuperação.

Em 24/09/2026, a política `boards_read` do projeto ativo recebeu um ajuste pontual:
o proprietário com perfil ativo é reconhecido diretamente na linha, antes das
regras existentes de leitura e participação em aprovações. Isso permite que
`INSERT ... RETURNING` devolva um projeto recém-criado sem afrouxar a política
de inserção. A mesma regra foi incorporada aos scripts
`boards_reliability_upgrade.sql` e `board_operations_integration_fix.sql` para
novas instalações. A verificação com papel `authenticated` e `ROLLBACK`
concluiu a inserção e confirmou que não restou projeto de teste.

Para uma instalação nova, execute na ordem:

1. `pulseboard_schema.sql`
2. `install_task_chat.sql`
3. `enterprise_upgrade.sql`
4. `security_hardening.sql`
5. `fix_tasks_rls_recursion.sql`
6. `boards_reliability_upgrade.sql`
7. `task_conversation_reliability_upgrade.sql` — completa conversa, imagens e menções e alinha as permissões à participação na demanda.
8. `notification_compatibility_fix.sql`
9. `respond_assignment_notification_fix.sql`
10. `task_assignment_trigger_fix.sql`
11. `board_operations_suite.sql` — colunas/WIP, ações em massa, auditoria por campo, aprovações, espelhos, dependências e SLA.
12. `remove_intake_forms.sql` — remove a estrutura legada de formulários públicos e seus dados.
13. `automation_project_scope_upgrade.sql` — pausa regras globais legadas e aplica automações apenas ao projeto configurado.
14. `board_operations_integration_fix.sql` — validação e integração de aprovações, substitutos, automações e ciclos de dependência.
15. `planning_workspace_upgrade.sql` — cockpit de planejamento, baseline transacional, dependências sem ciclos e recorrências idempotentes.
16. `planning_workspace_hardening.sql` — privilégios mínimos, políticas RLS sem sobreposição e índices dos relacionamentos de planejamento.
17. `planning_governance_scope_fix.sql` — limita alterações de baseline, vínculos, modelos e recorrências ao projeto ou equipe autorizada do gestor.
18. `billing_governance_upgrade.sql` — vincula contratos e faturas ao projeto, protege taxas históricas, controla aprovação, emissão e cancelamento de rascunhos.
19. `board_operation_profile_upgrade.sql` — classifica boards como entrega, suporte ou interno; preserva apontamentos e remove SLA apenas quando um board deixa de ser suporte.

Os scripts de upgrade são idempotentes sempre que possível. Alterações de produção devem ser aplicadas como migrações, nunca colando somente trechos isolados sem testar em uma transação.

## Verificações obrigatórias

- Usuário comum visualiza apenas Boards em que participa.
- Executor consegue aceitar, concluir, devolver e transferir uma atribuição.
- Uma tarefa não conclui com dependência ou checklist pendente.
- Arquivar preserva horas, comentários, arquivos e histórico.
- Edição com `row_version` antiga é recusada.
- Toda atribuição gera registro em `task_assignments` e alerta para o destinatário.
- A função `generate_billing_invoice` existe e a emissão de fatura ocorre em uma única transação.
- Uma fatura em rascunho cancelada libera exatamente seus apontamentos; faturas emitidas ou pagas permanecem imutáveis e exigem estorno em processo separado.
- Quando houver recorrências ativas, o job `pulseboard-recurring-tasks` está agendado e concluindo; não implemente um segundo executor na aplicação web.
