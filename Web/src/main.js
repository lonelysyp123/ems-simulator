import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import * as ElIcons from '@element-plus/icons-vue'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import App from './App.vue'
import router from './router.js'
import './styles/app.css'

async function bootstrap() {
  const app = createApp(App)
  for (const [key, comp] of Object.entries(ElIcons)) {
    app.component(key, comp)
  }
  app.use(ElementPlus, { locale: zhCn })
  app.use(router)
  app.mount('#app')
}

bootstrap()
