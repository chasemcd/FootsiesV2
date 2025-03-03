mergeInto(LibraryManager.library, {
    EmitUnityEpisodeResults: function(json) {        
        // Parse the JSON string back into an object
        window.eval(`
            try {
                const data = JSON.parse('${UTF8ToString(json)}');
                if (window.socket && window.socket.connected) {
                    window.socket.emit('unityEpisodeEnd', data);
                } else {
                    console.warn('Socket.IO is not connected. Cannot emit round results.');
                }
            } catch (e) {
                console.error('Error parsing/emitting round results:', e);
            }
        `);
    },
    
    UnityConnectSocketIO: function() {
        window.eval(`
            if (window.socket) {
                console.log('Socket.IO is already connected!');
            } else {
                console.error('Socket.IO connection is not established.');
            }
        `);
    },
    
    SetupUnitySocketListeners: function() {
        window.eval(`
            try {
                if (window.socket && window.socket.connected) {
                    window.socket.on('updateBotSettings', (data) => {
                        const jsonString = JSON.stringify(data);
                        unityInstance.SendMessage('SocketIOManager', 'updateBotSettings', jsonString);
                    });
                    
                    window.socket.on('toTitleScreen', (data) => {
                        unityInstance.SendMessage('SocketIOManager', 'OnToTitleScreen', data);
                    });
                    
                    console.log('Socket.IO listeners setup complete');
                } else {
                    console.warn('Socket.IO is not connected. Cannot setup listeners.');
                }
            } catch (e) {
                console.error('Error setting up Socket.IO listeners:', e);
            }
        `);
    }
});