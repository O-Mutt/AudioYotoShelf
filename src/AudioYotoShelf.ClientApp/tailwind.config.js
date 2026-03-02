/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{vue,js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        yoto: {
          blue: '#4A90D9',
          green: '#7BC67E',
          orange: '#F5A623',
          red: '#D0021B',
          purple: '#9B59B6',
        }
      }
    }
  },
  plugins: []
}
