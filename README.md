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

## Ambiente de teste no Render

O repositório inclui um `Dockerfile` e um Blueprint `render.yaml`. No Render,
crie um **Blueprint**, conecte este repositório e informe os três segredos que o
Blueprint solicitar:

- `Supabase__Url`
- `Supabase__AnonKey`
- `Supabase__ServiceRoleKey`

Os nomes usam `__` porque o ASP.NET Core converte essa notação para as chaves
`Supabase:Url`, `Supabase:AnonKey` e `Supabase:ServiceRoleKey`.

O serviço expõe `/health` para o health check do Render e escuta a porta `10000`
dentro do container. O plano gratuito hiberna quando fica sem tráfego e usa disco
efêmero; por isso, usuários autenticados precisarão entrar novamente depois de um
redeploy ou reinício.

Para conservar os cookies entre reinícios em um plano com disco persistente,
monte o disco (por exemplo, em `/var/data`) e configure também:

```text
DataProtection__KeysPath=/var/data/keys
```

Não exponha `Supabase__ServiceRoleKey` no navegador nem grave seu valor no Git.

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
