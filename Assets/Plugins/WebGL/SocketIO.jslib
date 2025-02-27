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
    }
});