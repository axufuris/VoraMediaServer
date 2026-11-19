import { useEffect, useState } from 'react';
import { libraryService, type LibrarySummary } from '../../../api/Media/libraryService';
import {
    webhookAdminService,
    type WebhookConnectionVM,
    type WebhookConnectionWithSecret,
    type WebhookRequestServerVM,
    type WebhookTemplateVM
} from '../../../api/System/webhookAdminService';
import { useDialog, type ConfirmOptions } from '../../../dialogs';
import { FieldLabel, Hint } from '../../../pages/Admin/SetupGuide/SetupParts';
import { Modal, ModalBody, ModalFooter, ModalHeader } from '../../Common/Modal';
import { AddressField, SecretField } from './ConnectionAccess';
import ConnectionSwitch from './ConnectionSwitch';
import RequestServerFill, { type RequestServerFillResult } from './RequestServerFill';
import SamplePayload from './SamplePayload';
import SetupInstructions from './SetupInstructions';
import TemplateSettings from './TemplateSettings';
import { hasMissingRequired, initialSettings, setupSteps, webhookErrorMessage } from './connectionForm';
import { FOCUS_FILL, FOCUS_FILL_PRIMARY, receiveUrl } from './libraryUpdates';

interface ConnectionModalProps {
    connection: WebhookConnectionVM | null;
    templates: WebhookTemplateVM[];
    serverId?: string;
    onChanged: () => void;
    onClose: () => void;
}

const CLOSE_BUTTON = `rounded-md px-2 ${FOCUS_FILL}`;
const CARD = 'flex flex-col max-h-[90vh] max-md:max-h-[calc(100dvh-2rem)]';
const BODY = 'min-h-0 space-y-5 max-md:p-4';

export default function ConnectionModal({ connection, templates, serverId, onChanged, onClose }: ConnectionModalProps) {
    const dialog = useDialog();
    const connectionId = connection?.id;
    const editing = connection !== null;

    const [templateId, setTemplateId] = useState(connection?.providerId ?? templates[0]?.id ?? '');
    const template = templates.find(candidate => candidate.id === templateId) ?? null;
    const definitions = template?.settings ?? [];

    const [name, setName] = useState(connection?.name ?? template?.defaultConnectionName ?? '');
    const [filledName, setFilledName] = useState<string | null>(null);
    const [isEnabled, setIsEnabled] = useState(connection?.isEnabled ?? true);
    const [handleDeletes, setHandleDeletes] = useState(connection?.handleDeletes ?? true);
    const [pathMappings, setPathMappings] = useState(connection?.pathMappings ?? '');
    const [settings, setSettings] = useState(() => initialSettings(definitions, connection?.settings));

    const [secret, setSecret] = useState<string | null>(null);
    const [secretError, setSecretError] = useState<string | null>(null);
    const [regenerating, setRegenerating] = useState(false);
    const [libraries, setLibraries] = useState<LibrarySummary[] | null>(null);
    const [librariesFailed, setLibrariesFailed] = useState(false);
    const [requestServers, setRequestServers] = useState<WebhookRequestServerVM[]>([]);
    const [created, setCreated] = useState<WebhookConnectionWithSecret | null>(null);
    const [saving, setSaving] = useState(false);
    const [deleting, setDeleting] = useState(false);
    const [asking, setAsking] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const needsLibraries = definitions.some(definition => definition.type === 'library');

    useEffect(() => {
        if (!connectionId) return;
        let cancelled = false;
        webhookAdminService.getConnection(connectionId, serverId)
            .then(detail => { if (!cancelled) setSecret(detail.secret); })
            .catch(() => { if (!cancelled) setSecretError('The secret could not be loaded.'); });
        return () => { cancelled = true; };
    }, [connectionId, serverId]);

    useEffect(() => {
        if (!needsLibraries) return;
        let cancelled = false;
        libraryService.getLibraries(serverId)
            .then(list => { if (!cancelled) setLibraries(list); })
            .catch(() => {
                if (cancelled) return;
                setLibraries([]);
                setLibrariesFailed(true);
            });
        return () => { cancelled = true; };
    }, [needsLibraries, serverId]);

    useEffect(() => {
        if (editing) return;
        let cancelled = false;
        webhookAdminService.getRequestServers(serverId)
            .then(list => { if (!cancelled) setRequestServers(list); })
            .catch(() => { if (!cancelled) setRequestServers([]); });
        return () => { cancelled = true; };
    }, [editing, serverId]);

    const ask = async (options: ConfirmOptions) => {
        setAsking(true);
        try {
            return await dialog.confirm(options);
        } finally {
            setAsking(false);
        }
    };

    const chooseTemplate = (id: string) => {
        const next = templates.find(candidate => candidate.id === id);
        if (!next) return;
        setName(current => (!current.trim() || current === template?.defaultConnectionName || current === filledName ? next.defaultConnectionName : current));
        setFilledName(null);
        setSettings(initialSettings(next.settings));
        setTemplateId(id);
        setError(null);
    };

    const applyFill = (fill: RequestServerFillResult) => {
        if (fill.name) {
            setName(fill.name);
            setFilledName(fill.name);
        }
        if (fill.mappings) setPathMappings(fill.mappings);
    };

    const setSetting = (key: string, value: string) => setSettings(current => ({ ...current, [key]: value }));

    const matchingServers = template?.requestServerProviderId ? requestServers.filter(server => server.webhookProviderId === template.id) : [];
    const valid = !!template && name.trim().length > 0 && !hasMissingRequired(definitions, settings);
    const busy = saving || deleting;

    const submit = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!template || !valid || busy) return;
        setSaving(true);
        setError(null);
        const body = { name: name.trim(), isEnabled, handleDeletes, pathMappings, settings };
        try {
            if (connection) {
                await webhookAdminService.updateConnection(connection.id, body, serverId);
                onChanged();
                onClose();
            } else {
                setCreated(await webhookAdminService.createConnection({ ...body, providerId: template.id }, serverId));
                onChanged();
            }
        } catch (err) {
            setError(webhookErrorMessage(err, 'The connection could not be saved.'));
        } finally {
            setSaving(false);
        }
    };

    const remove = async () => {
        if (!connection) return;
        const confirmed = await ask({
            title: 'Delete this connection?',
            message: `${connection.name} is removed and its address and secret stop working. Update ${connection.providerName} so it no longer sends to it.`,
            confirmText: 'Delete connection',
            cancelText: 'Cancel',
            tone: 'danger'
        });
        if (!confirmed) return;
        setDeleting(true);
        setError(null);
        try {
            await webhookAdminService.deleteConnection(connection.id, serverId);
            onChanged();
            onClose();
        } catch (err) {
            setError(webhookErrorMessage(err, 'The connection could not be deleted.'));
            setDeleting(false);
        }
    };

    const regenerate = async (): Promise<boolean> => {
        if (!connection) return false;
        const confirmed = await ask({
            title: 'Regenerate the secret?',
            message: "The current secret stops working at once. The connection in the app that sends to Vora must then be updated with the new secret, or Vora will refuse what it sends.",
            confirmText: 'Regenerate secret',
            cancelText: 'Cancel',
            tone: 'danger'
        });
        if (!confirmed) return false;
        setRegenerating(true);
        setError(null);
        try {
            const generated = await webhookAdminService.regenerateSecret(connection.id, serverId);
            setSecret(generated.secret);
            setSecretError(null);
            return true;
        } catch (err) {
            setError(webhookErrorMessage(err, 'The secret could not be regenerated.'));
            return false;
        } finally {
            setRegenerating(false);
        }
    };

    const providerName = connection?.providerName ?? template?.name ?? 'the app';

    if (created) {
        return (
            <Modal isOpen onClose={onClose} size="3xl" surface="light" cardClassName={CARD} closeOnEscape={!asking}>
                <ModalHeader title="Connection created" subtitle={created.name} onClose={onClose} surface="light" closeClassName={CLOSE_BUTTON} />
                <ModalBody className={BODY}>
                    <p role="status" className="rounded-[var(--vora-radius-md)] border border-[var(--vora-accent-500)] bg-[var(--vora-accent-soft)] p-3 text-sm text-[var(--vora-accent-text)]">
                        Paste the address and the secret into {created.providerName}. The secret can be shown again by opening this connection.
                    </p>
                    <AddressField url={receiveUrl(created.receivePath, serverId)} providerName={created.providerName} />
                    <SecretField secret={created.secret} initiallyRevealed />
                    <SetupInstructions steps={setupSteps(template?.setupInstructions ?? '')} defaultOpen />
                </ModalBody>
                <ModalFooter className="flex justify-end gap-2">
                    <button type="button" onClick={onClose} className={`vora-button-primary ${FOCUS_FILL_PRIMARY}`}>Done</button>
                </ModalFooter>
            </Modal>
        );
    }

    return (
        <Modal isOpen onClose={onClose} size="3xl" surface="light" cardClassName={CARD} closeOnEscape={!asking}>
            <ModalHeader
                title={editing ? 'Edit connection' : 'Add connection'}
                subtitle={editing ? connection.name : undefined}
                onClose={onClose}
                surface="light"
                closeClassName={CLOSE_BUTTON}
            />
            <form onSubmit={submit} className="flex min-h-0 flex-1 flex-col">
                <ModalBody className={BODY}>
                    {!editing && (
                        <div className="space-y-3">
                            <div>
                                <FieldLabel htmlFor="webhook-template">Template</FieldLabel>
                                <select
                                    id="webhook-template"
                                    value={templateId}
                                    onChange={e => chooseTemplate(e.target.value)}
                                    className={`vora-input w-full cursor-pointer`}
                                >
                                    {templates.map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.name}</option>)}
                                </select>
                                {template && <Hint>{template.description}</Hint>}
                            </div>
                            {matchingServers.length > 0 && template && (
                                <RequestServerFill
                                    key={template.id}
                                    servers={matchingServers}
                                    serverId={serverId}
                                    mappings={pathMappings}
                                    nameIsUntouched={!name.trim() || name === template.defaultConnectionName || name === filledName}
                                    confirm={ask}
                                    onFill={applyFill}
                                />
                            )}
                        </div>
                    )}

                    <div>
                        <FieldLabel htmlFor="webhook-name">Name <span aria-hidden="true">*</span></FieldLabel>
                        <input
                            id="webhook-name"
                            type="text"
                            value={name}
                            onChange={e => setName(e.target.value)}
                            aria-required="true"
                            className="vora-input w-full"
                        />
                    </div>

                    <div className="space-y-2">
                        <ConnectionSwitch id="webhook-enabled" label="On" checked={isEnabled} onChange={setIsEnabled} />
                        <ConnectionSwitch
                            id="webhook-handle-deletes"
                            label="Move deleted files to Media Trash"
                            description="When this is off, deletes the app reports are ignored."
                            checked={handleDeletes}
                            onChange={setHandleDeletes}
                        />
                    </div>

                    {connection && (
                        <div className="space-y-4">
                            <AddressField url={receiveUrl(connection.receivePath, serverId)} providerName={providerName} />
                            <SecretField secret={secret} loadError={secretError} regenerating={regenerating} onRegenerate={regenerate} />
                        </div>
                    )}

                    <div>
                        <FieldLabel htmlFor="webhook-path-mappings">Path mappings</FieldLabel>
                        <textarea
                            id="webhook-path-mappings"
                            rows={3}
                            value={pathMappings}
                            onChange={e => setPathMappings(e.target.value)}
                            spellCheck={false}
                            placeholder="/tv => /media/tv"
                            className="vora-input w-full font-mono text-xs"
                        />
                        <Hint>One per line, from =&gt; to: the folder as the app sees it, then as Vora sees it. Leave this empty when both use the same paths. Vora also matches by folder name.</Hint>
                    </div>

                    <TemplateSettings
                        definitions={definitions}
                        values={settings}
                        libraries={libraries}
                        librariesFailed={librariesFailed}
                        onChange={setSetting}
                    />

                    <SetupInstructions steps={setupSteps(template?.setupInstructions ?? '')} defaultOpen={!editing} />

                    {template && <SamplePayload providerId={template.id} settings={settings} pathMappings={pathMappings} serverId={serverId} />}

                    {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}
                </ModalBody>
                <ModalFooter className="flex flex-wrap items-center justify-between gap-3 max-md:p-4">
                    {editing ? (
                        <button type="button" onClick={remove} disabled={busy} className={`vora-button-secondary text-[var(--vora-danger-text)] disabled:opacity-50 ${FOCUS_FILL}`}>
                            {deleting ? 'Deleting…' : 'Delete connection'}
                        </button>
                    ) : <span />}
                    <div className="flex items-center gap-2">
                        <button type="button" onClick={onClose} className={`vora-button-secondary ${FOCUS_FILL}`}>Cancel</button>
                        <button
                            type="submit"
                            disabled={!valid || busy}
                            className={`vora-button-primary ${FOCUS_FILL_PRIMARY}`}
                        >
                            {saving ? 'Saving…' : 'Save'}
                        </button>
                    </div>
                </ModalFooter>
            </form>
        </Modal>
    );
}
