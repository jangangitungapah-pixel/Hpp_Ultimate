window.recipeEditor = {
    focusFirst(selector) {
        window.setTimeout(() => {
            const element = document.querySelector(selector);
            if (!element) {
                return;
            }

            element.focus();
            if (typeof element.select === "function") {
                element.select();
            }
        }, 40);
    }
};
