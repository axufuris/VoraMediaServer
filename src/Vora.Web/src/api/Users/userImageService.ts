import { apiClient } from '../client';

export const userImageService = {
    uploadImage: async (file: File, oldUrl?: string, serverId?: string): Promise<string> => {
        const formData = new FormData();
        formData.append('file', file);
        if (oldUrl) formData.append('oldUrl', oldUrl);

        const response = await apiClient.post<{ url: string }>('/users/images/upload', formData, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    }
};
