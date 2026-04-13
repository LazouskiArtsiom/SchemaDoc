window.globalSearch = {
    init: function (dotNetRef) {
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnCtrlK');
            }
        });
    },
    focusInput: function (elementId) {
        setTimeout(function () {
            var el = document.getElementById(elementId);
            if (el) el.focus();
        }, 50);
    }
};
