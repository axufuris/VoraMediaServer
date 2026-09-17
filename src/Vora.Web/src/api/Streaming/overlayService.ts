import { apiClient } from '../client';

export interface OverlayTemplateDto {
    id?: string;
    name?: string;
    targetMediaType: string;
    targetLibraryId?: string | null;
    configurationJson: string;
}

export const overlayService = {
    getTemplates: async (serverId?: string): Promise<OverlayTemplateDto[]> => {
        const emptyGuid = '00000000-0000-0000-0000-000000000000';
        const response = await apiClient.get<OverlayTemplateDto[]>(`/overlays/templates/${emptyGuid}`, { serverId });
        return response.data;
    },

    getTemplateByMediaType: async (mediaType: string, serverId?: string): Promise<OverlayTemplateDto | null> => {
        const templates = await overlayService.getTemplates(serverId);
        return templates.find(t => t.targetMediaType === mediaType) || null;
    },

    saveTemplate: async (payload: OverlayTemplateDto, serverId?: string): Promise<void> => {
        const existingTemplate = await overlayService.getTemplateByMediaType(payload.targetMediaType, serverId);

        if (existingTemplate && existingTemplate.id) {
            await apiClient.put(`/overlays/templates/${existingTemplate.id}`, payload, { serverId });
        } else {
            await apiClient.post('/overlays/templates', payload, { serverId });
        }
    },

    deleteTemplate: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/overlays/templates/${id}`, { serverId });
    },

    triggerGlobalSync: async (serverId?: string): Promise<void> => {
        const emptyGuid = '00000000-0000-0000-0000-000000000000';
        await apiClient.post(`/overlays/templates/sync-library/${emptyGuid}`, {}, { serverId });
    }
};