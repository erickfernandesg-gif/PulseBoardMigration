# PulseBoard — migração ASP.NET Core MVC

Migração do PulseBoard original (Next.js/React) para ASP.NET Core MVC em .NET 8,
mantendo o Supabase como autenticação e banco PostgreSQL.

## Pré-requisitos

- .NET SDK 8
- Projeto Supabase
- Node.js não é necessário

## Configuração

Nunca grave chaves reais no Git. Configure com User Secrets ou variáveis de ambiente:

```powershell
dotnet user-secrets init
dotnet user-secrets set "Supabase:Url" "https://SEU-PROJETO.supabase.co"
dotnet user-secrets set "Supabase:AnonKey" "SUA-CHAVE-PUBLICA"
dotnet user-secrets set "Supabase:ServiceRoleKey" "SUA-CHAVE-SERVICE-ROLE"
```

`ServiceRoleKey` é usada apenas no servidor para operações administrativas e para
armazenar/servir anexos privados da conversa das tarefas. Ela nunca deve ser enviada
ao navegador. As consultas de metadados continuam usando a sessão do usuário e RLS.

Execute [Database/pulseboard_schema.sql](Database/pulseboard_schema.sql) no SQL
Editor do Supabase antes de iniciar a aplicação.

## Execução

```powershell
dotnet restore
dotnet run
```

## Módulos

- Autenticação e perfil
- Dashboard operacional
- Quadros em Kanban, tabela e Gantt
- Planejamento, responsáveis, clientes, ciclos e estimativas
- Conversa da atividade com imagens privadas, edição, exclusão auditável e alertas
- Checklists, colaboradores e apontamentos de horas
- Administração de usuários, equipes, clientes e custo/hora
- Automações por mudança de status
- Formulários públicos
- Relatórios exportáveis
- Painel executivo de capacidade e custo
- Meu trabalho com caixa de entrada, execução, espera e aceite
- Handoff entre etapas com aceite, recusa, conclusão e solicitação de ajustes
- Notificações individuais de atribuição, comentários e prazos
- Gestão de capacidade semanal e riscos da equipe
- Cronograma corporativo entre projetos, marcos e dependências
- Contratos, aprovação de horas, valores históricos e geração de faturas

## Atualização do banco

O arquivo `Database/pulseboard_schema.sql` também funciona como migração idempotente.
Após atualizar o código, execute o arquivo completo novamente no SQL Editor do
Supabase. Isso cria as tabelas de notificações, atribuições, seguidores,
dependências, marcos, capacidade, contratos e faturamento, além das funções
transacionais usadas nos handoffs.

O fluxo recomendado é:

1. Criar ou atribuir uma tarefa.
2. O executor aceita a atribuição em **Meu trabalho**.
3. Dúvidas podem ser registradas em **Devolver com dúvida**; a ação volta para a
   pessoa escolhida e o executor anterior continua acompanhando.
4. Ao terminar sua parte, usa **Enviar para a próxima etapa**.
5. O executor anterior passa a acompanhar em **Aguardando terceiros**.
6. Um pré-requisito impede o aceite da atividade enquanto a tarefa necessária não
   estiver concluída (por exemplo, “Publicar” depende de “Testar”).
7. A última etapa pode exigir aceite do responsável principal.
8. Apontamentos são aprovados em **Faturamento** antes de compor uma fatura.
